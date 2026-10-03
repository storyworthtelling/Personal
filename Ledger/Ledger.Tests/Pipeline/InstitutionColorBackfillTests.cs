using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Pipeline;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.Pipeline;
using Index.Tests.Support;

namespace Index.Tests.Pipeline;

// R-G8. A bank's primary_color decides the tint of every post-it that bears its name, and the whole
// tint pipeline was unreachable because nothing ever fetched the colour. The lookup rides the balance
// refresh as a once-only backfill. Both ways to get it wrong are SILENT: never asking leaves every
// post-it default yellow; asking on every refresh spends a Plaid call per bank per day forever for an
// answer that never changes. Nothing fails visibly either way, so this pins the count.
public class InstitutionColorBackfillTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string InstitutionId = "ins_13";

    // The lookup passes Plaid's colour through untouched (PostItTint is what parses it), so these tests
    // use an opaque marker rather than a hex literal the colour-leak guard would rightly object to.
    private const string PncColour = "plaid-primary-colour";

    private sealed class CapturingLogger : ILogger<BalanceRefreshProcessor>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static BalanceRefreshProcessor Create(
        TestDatabase db, IPlaidClient plaid, FakeClock clock, ILogger<BalanceRefreshProcessor> logger) =>
        new(db, plaid, clock,
            Options.Create(new RefreshOptions
            {
                RefreshIntervalHours = 24,
                MaxAttempts = 5,
                BaseDelaySeconds = 30,
                MaxDelaySeconds = 3600
            }),
            logger);

    private static Guid Seed(
        TestDatabase db, string? plaidInstitutionId = InstitutionId, string? colour = null,
        DateTimeOffset? lookedUp = null)
    {
        using var context = db.CreateDbContext();
        var institution = new Institution
        {
            ItemId = "item-1",
            AccessToken = "access-1",
            Name = "PNC",
            CreatedAtUtc = Now,
            PlaidInstitutionId = plaidInstitutionId,
            PrimaryColorHex = colour,
            ColorLookedUpAtUtc = lookedUp
        };
        context.Institutions.Add(institution);
        context.SaveChanges();
        return institution.Id;
    }

    private static Mock<IPlaidClient> PlaidWithBalances()
    {
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlaidAccount("acc1", "Checking", null, "1234", "depository", "checking", "USD", 110m, 100m, null)
            });
        return plaid;
    }

    // Runs `count` refreshes, each one a day apart so the institution is due every time.
    private static async Task RefreshRepeatedlyAsync(
        BalanceRefreshProcessor processor, FakeClock clock, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            Assert.Equal(ProcessOutcome.DidWork, result.Outcome);
            clock.Advance(TimeSpan.FromHours(25));
        }
    }

    private static Institution Load(TestDatabase db, Guid id)
    {
        using var context = db.CreateDbContext();
        return context.Institutions.AsNoTracking().Single(i => i.Id == id);
    }

    [Fact]
    public async Task NoStoredColour_RepeatedRefreshes_LookUpExactlyOnce_AndStoreIt()
    {
        using var db = new TestDatabase();
        var id = Seed(db);
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();
        plaid.Setup(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaidInstitution(InstitutionId, "PNC Bank", PncColour));

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 3);

        plaid.Verify(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()), Times.Once);
        var institution = Load(db, id);
        Assert.Equal(PncColour, institution.PrimaryColorHex);
        Assert.NotNull(institution.ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task PlaidAnsweredWithNoColour_IsRecordedAsLookedUp_AndNotAskedAgain()
    {
        using var db = new TestDatabase();
        var id = Seed(db);
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();
        plaid.Setup(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaidInstitution(InstitutionId, "Small Credit Union", null));

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 3);

        plaid.Verify(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()), Times.Once);
        var institution = Load(db, id);
        Assert.Null(institution.PrimaryColorHex);
        Assert.NotNull(institution.ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task StoredColour_IsNeverFetchedAgain()
    {
        using var db = new TestDatabase();
        var id = Seed(db, colour: PncColour);
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 2);

        plaid.Verify(p => p.GetInstitutionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        plaid.Verify(p => p.GetItemInstitutionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(PncColour, Load(db, id).PrimaryColorHex);
    }

    [Fact]
    public async Task AlreadyLookedUp_IsNeverFetchedAgain_EvenWithNoColour()
    {
        using var db = new TestDatabase();
        Seed(db, lookedUp: Now.AddDays(-3));
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 2);

        plaid.Verify(p => p.GetInstitutionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rate-limit")]
    [InlineData("network")]
    public async Task FailedLookup_NeverFailsTheRefresh_IsLogged_AndIsRetriedNextRefresh(string failure)
    {
        using var db = new TestDatabase();
        var id = Seed(db);
        var clock = new FakeClock(Now);
        var logger = new CapturingLogger();
        var plaid = PlaidWithBalances();
        var calls = 0;
        plaid.Setup(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                if (calls == 1)
                    throw failure == "rate-limit"
                        ? new RateLimitException("Plaid rate limited: 429")
                        : new HttpRequestException("The network is unreachable.");
                return Task.FromResult(new PlaidInstitution(InstitutionId, "PNC Bank", PncColour));
            });
        var processor = Create(db, plaid.Object, clock, logger);

        await processor.ProcessNextAsync(CancellationToken.None);

        // The refresh itself is untouched by the failed lookup: balances written, institution Completed.
        var afterFailure = Load(db, id);
        Assert.Equal(WorkStatus.Completed, afterFailure.RefreshStatus);
        Assert.Equal(0, afterFailure.Attempts);
        Assert.Null(afterFailure.LastError);
        Assert.Null(afterFailure.PrimaryColorHex);
        // ...and nothing says "looked up", so the next refresh asks again.
        Assert.Null(afterFailure.ColorLookedUpAtUtc);
        // ...and the failure went to the logger, not into thin air.
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("PNC"));

        clock.Advance(TimeSpan.FromHours(25));
        await processor.ProcessNextAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(25));
        await processor.ProcessNextAsync(CancellationToken.None);

        plaid.Verify(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        var final = Load(db, id);
        Assert.Equal(PncColour, final.PrimaryColorHex);
        Assert.NotNull(final.ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task BankConnectedBeforeItsInstitutionIdWasStored_AsksPlaidWhichBankItIs_Once()
    {
        using var db = new TestDatabase();
        var id = Seed(db, plaidInstitutionId: null);
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();
        plaid.Setup(p => p.GetItemInstitutionIdAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstitutionId);
        plaid.Setup(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaidInstitution(InstitutionId, "PNC Bank", PncColour));

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 3);

        plaid.Verify(p => p.GetItemInstitutionIdAsync("access-1", It.IsAny<CancellationToken>()), Times.Once);
        plaid.Verify(p => p.GetInstitutionAsync(InstitutionId, It.IsAny<CancellationToken>()), Times.Once);
        var institution = Load(db, id);
        Assert.Equal(InstitutionId, institution.PlaidInstitutionId);
        Assert.Equal(PncColour, institution.PrimaryColorHex);
    }

    [Fact]
    public async Task ItemWithNoInstitution_IsRecordedAsLookedUp_WithoutAskingForADirectoryEntry()
    {
        using var db = new TestDatabase();
        var id = Seed(db, plaidInstitutionId: null);
        var clock = new FakeClock(Now);
        var plaid = PlaidWithBalances();
        plaid.Setup(p => p.GetItemInstitutionIdAsync("access-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await RefreshRepeatedlyAsync(Create(db, plaid.Object, clock, new CapturingLogger()), clock, 3);

        plaid.Verify(p => p.GetItemInstitutionIdAsync("access-1", It.IsAny<CancellationToken>()), Times.Once);
        plaid.Verify(p => p.GetInstitutionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(Load(db, id).ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task FailedBalanceRefresh_DoesNotSpendALookup()
    {
        using var db = new TestDatabase();
        Seed(db);
        var clock = new FakeClock(Now);
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        await Create(db, plaid.Object, clock, new CapturingLogger()).ProcessNextAsync(CancellationToken.None);

        plaid.Verify(p => p.GetInstitutionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
