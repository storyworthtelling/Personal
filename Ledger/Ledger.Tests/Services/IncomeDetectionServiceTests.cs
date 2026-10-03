using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Guard G22's Infrastructure half (D104): <see cref="IncomeDetectionService"/> only ever asks Plaid
/// for transactions on institutions THIS install actually connected — a hand-entered account (no
/// access token) is skipped exactly the way <c>BudgetService.RefreshTransactionsAsync</c> already
/// skips it, and an account nobody connected at all is never a row here to begin with. The algorithm
/// itself (<see cref="IncomeDetector"/>) is exercised directly and adversarially in
/// <c>Guard.IncomeProjectionTests</c>; this file only proves the wiring around it.
/// </summary>
public class IncomeDetectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static PlaidTransaction Deposit(DateTimeOffset date, decimal amount) =>
        new(Guid.NewGuid().ToString(), "acct-1", date, "Employer", null, (double)(-amount), "USD", Pending: false);

    [Fact]
    public async Task DetectAsync_NeverQueriesPlaid_ForAHandEnteredInstitution()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.Institutions.Add(new Institution
            {
                Name = "Cash Under the Mattress",
                AccessToken = string.Empty, // hand-entered: no Plaid Item behind it
                Accounts = [new Account { AccountId = "manual-1", Name = "Envelope" }],
            });
            await context.SaveChangesAsync();
        }

        var plaid = new Mock<IPlaidClient>(MockBehavior.Strict);
        var service = new IncomeDetectionService(db, new FakeClock(Now), plaid.Object);

        var estimate = await service.DetectAsync(default);

        // MockBehavior.Strict means any unexpected call (i.e. GetTransactionsAsync on the hand-entered
        // institution's non-existent access token) throws — reaching this line at all proves it wasn't.
        Assert.False(estimate.IsConfident);
    }

    [Fact]
    public async Task DetectAsync_AggregatesTransactionsAcrossEveryPlaidConnectedInstitution()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.Institutions.Add(new Institution
            {
                Name = "Redwood Bank",
                AccessToken = "access-redwood",
                Accounts = [new Account { AccountId = "redwood-checking", Name = "Checking" }],
            });
            context.Institutions.Add(new Institution
            {
                Name = "PNC",
                AccessToken = "access-pnc",
                Accounts = [new Account { AccountId = "pnc-checking", Name = "Checking" }],
            });
            await context.SaveChangesAsync();
        }

        // A biweekly $2,400 paycheck landing on Redwood Bank, ten occurrences — split across two "institution"
        // calls only in the sense that the fake below answers per access token, matching how Plaid
        // itself scopes a GetTransactionsAsync call to one Item.
        var redwoodDeposits = Enumerable.Range(0, 10)
            .Select(i => Deposit(new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero).AddDays(i * 14), 2400m))
            .ToList();

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-redwood", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(redwoodDeposits);
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-pnc", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlaidTransaction>)[]);

        var service = new IncomeDetectionService(db, new FakeClock(Now), plaid.Object);

        var estimate = await service.DetectAsync(default);

        Assert.True(estimate.IsConfident);
        Assert.Equal(5200m, estimate.ProjectedMonthlyIncome);
    }

    [Fact]
    public async Task DetectAsync_OneInstitutionsFailure_NeverBlocksTheOthers()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateDbContext())
        {
            context.Institutions.Add(new Institution
            {
                Name = "Broken Bank",
                AccessToken = "access-broken",
                Accounts = [new Account { AccountId = "broken-checking", Name = "Checking" }],
            });
            context.Institutions.Add(new Institution
            {
                Name = "Redwood Bank",
                AccessToken = "access-redwood",
                Accounts = [new Account { AccountId = "redwood-checking", Name = "Checking" }],
            });
            await context.SaveChangesAsync();
        }

        var deposits = Enumerable.Range(0, 6)
            .Select(i => Deposit(new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero).AddMonths(i), 5000m))
            .ToList();

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-broken", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transactions product not ready"));
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-redwood", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposits);

        var service = new IncomeDetectionService(db, new FakeClock(Now), plaid.Object);

        var estimate = await service.DetectAsync(default);

        Assert.True(estimate.IsConfident);
        Assert.Equal(5000m, estimate.ProjectedMonthlyIncome);
    }
}
