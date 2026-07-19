using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tally.Domain.External;
using Tally.Domain.Model;
using Tally.Domain.Pipeline;
using Tally.Infrastructure.Configuration;
using Tally.Infrastructure.Pipeline;
using Tally.Tests.Support;

namespace Tally.Tests.Pipeline;

// The refresh step the worker drains. State lives entirely in SQLite so a refresh resumes across
// wakeups; these tests pin the due-selection, upsert, liabilities, and failure/backoff behavior.
public class BalanceRefreshProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static BalanceRefreshProcessor Create(
        TestDatabase db, IPlaidClient plaid, FakeClock clock, int maxAttempts = 5) =>
        new(db, plaid, clock,
            Options.Create(new RefreshOptions
            {
                RefreshIntervalHours = 24,
                MaxAttempts = maxAttempts,
                BaseDelaySeconds = 30,
                MaxDelaySeconds = 3600
            }),
            NullLogger<BalanceRefreshProcessor>.Instance);

    private static Guid SeedInstitution(
        TestDatabase db, WorkStatus status = WorkStatus.Pending, DateTimeOffset? nextAttempt = null)
    {
        using var context = db.CreateDbContext();
        var institution = new Institution
        {
            ItemId = "item-1",
            AccessToken = "access-1",
            Name = "PNC",
            RefreshStatus = status,
            NextAttemptAtUtc = nextAttempt,
            CreatedAtUtc = Now
        };
        context.Institutions.Add(institution);
        context.SaveChanges();
        return institution.Id;
    }

    private static PlaidAccount Depository(string id) =>
        new(id, "Checking", "Gold Checking", "1234", "depository", "checking", "USD", 110.0, 100.0, null);

    private static PlaidAccount Credit(string id) =>
        new(id, "Rewards Card", null, "5678", "credit", "credit card", "USD", -500.0, null, 1000.0);

    [Fact]
    public async Task DueInstitution_WritesBalances_MarksCompleted_SchedulesNextDay()
    {
        using var db = new TestDatabase();
        var id = SeedInstitution(db);
        var clock = new FakeClock(Now);

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Depository("acc1") });

        var result = await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);

        using var context = db.CreateDbContext();
        var institution = context.Institutions.Include(i => i.Accounts).Single(i => i.Id == id);
        Assert.Equal(WorkStatus.Completed, institution.RefreshStatus);
        Assert.Equal(0, institution.Attempts);
        Assert.Null(institution.LastError);
        Assert.Equal(Now, institution.LastRefreshedUtc);
        Assert.Equal(Now.AddHours(24), institution.NextAttemptAtUtc);

        var account = Assert.Single(institution.Accounts);
        Assert.Equal("acc1", account.AccountId);
        Assert.Equal(AccountKind.Depository, account.Kind);
        Assert.Equal(110.0, account.CurrentBalance);
        Assert.Equal(Now, account.BalanceAsOfUtc);

        // A depository-only Item needs no Liabilities call.
        plaid.Verify(p => p.GetLiabilitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreditAccounts_FetchLiabilities_AndMergeDetail()
    {
        using var db = new TestDatabase();
        var id = SeedInstitution(db);
        var clock = new FakeClock(Now);
        var due = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Credit("card1") });
        plaid.Setup(p => p.GetLiabilitiesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PlaidLiability("card1", 500.25, due, 25.0, 19.99) });

        await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        using var context = db.CreateDbContext();
        var account = context.Accounts.Single(a => a.InstitutionId == id);
        Assert.Equal(AccountKind.Credit, account.Kind);
        Assert.Equal(500.25, account.LastStatementBalance);
        Assert.Equal(25.0, account.NextPaymentAmount);
        Assert.Equal(19.99, account.Apr);
        Assert.Equal(due, account.NextPaymentDueDate);
        plaid.Verify(p => p.GetLiabilitiesAsync("access-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingAccount_IsUpdatedInPlace_NotDuplicated()
    {
        using var db = new TestDatabase();
        var id = SeedInstitution(db);
        var clock = new FakeClock(Now);

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Depository("acc1") });
        var processor = Create(db, plaid.Object, clock);

        await processor.ProcessNextAsync(CancellationToken.None);
        // Make it due again and refresh a second time with a changed balance.
        using (var ctx = db.CreateDbContext())
        {
            var inst = ctx.Institutions.Single(i => i.Id == id);
            inst.NextAttemptAtUtc = null;
            inst.RefreshStatus = WorkStatus.Pending;
            ctx.SaveChanges();
        }
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlaidAccount("acc1", "Checking", "Gold Checking", "1234", "depository", "checking", "USD", 222.0, 200.0, null)
            });
        await processor.ProcessNextAsync(CancellationToken.None);

        using var context = db.CreateDbContext();
        var account = Assert.Single(context.Accounts.Where(a => a.InstitutionId == id));
        Assert.Equal(222.0, account.CurrentBalance);
    }

    [Fact]
    public async Task RateLimited_MarksNeedsRetry_WithBackoff_WithoutFailing()
    {
        using var db = new TestDatabase();
        var id = SeedInstitution(db);
        var clock = new FakeClock(Now);

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitException("throttled"));

        var result = await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);
        using var context = db.CreateDbContext();
        var institution = context.Institutions.Single(i => i.Id == id);
        Assert.Equal(WorkStatus.NeedsRetry, institution.RefreshStatus);
        Assert.NotNull(institution.NextAttemptAtUtc);
        Assert.True(institution.NextAttemptAtUtc > Now);
    }

    [Fact]
    public async Task HardError_ReachingMaxAttempts_MarksFailed()
    {
        using var db = new TestDatabase();
        var id = SeedInstitution(db);
        var clock = new FakeClock(Now);

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        await Create(db, plaid.Object, clock, maxAttempts: 1).ProcessNextAsync(CancellationToken.None);

        using var context = db.CreateDbContext();
        var institution = context.Institutions.Single(i => i.Id == id);
        Assert.Equal(WorkStatus.Failed, institution.RefreshStatus);
        Assert.Null(institution.NextAttemptAtUtc);
        Assert.Equal("boom", institution.LastError);
    }

    [Fact]
    public async Task NotYetDue_ReturnsWaiting_ForSoonestInstitution()
    {
        using var db = new TestDatabase();
        SeedInstitution(db, WorkStatus.Completed, Now.AddHours(1));
        var clock = new FakeClock(Now);

        var plaid = new Mock<IPlaidClient>();
        var result = await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Waiting, result.Outcome);
        Assert.Equal(TimeSpan.FromHours(1), result.Wait);
        plaid.Verify(p => p.GetBalancesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoInstitutions_ReturnsIdle()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var plaid = new Mock<IPlaidClient>();

        var result = await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Idle, result.Outcome);
    }

    [Fact]
    public async Task FailedInstitution_IsSkipped()
    {
        using var db = new TestDatabase();
        SeedInstitution(db, WorkStatus.Failed);
        var clock = new FakeClock(Now);
        var plaid = new Mock<IPlaidClient>();

        var result = await Create(db, plaid.Object, clock).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Idle, result.Outcome);
        plaid.Verify(p => p.GetBalancesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
