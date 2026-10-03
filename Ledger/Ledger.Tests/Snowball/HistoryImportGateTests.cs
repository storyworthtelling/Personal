using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Infrastructure.Snowball;
using Index.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>
/// The import used to run only when "no snapshot exists yet", with the history compiled into the app. Now the
/// history is read from the owner's Drive, and that read can fail — so the gate must not be closed by
/// something other than the history itself. The weekly snapshot the page writes straight after the import
/// attempt is exactly that: a failed read followed by <c>RecordCurrentWeekSnapshotAsync</c> would have
/// shut the gate for good with nothing imported. The gate is therefore "no snapshot dated on or before the
/// history's last recorded week", which the weekly rows (dated after it) can never satisfy.
/// </summary>
public class HistoryImportGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static async Task SeedAllTenAsync(TestDatabase db, bool withApr = false)
    {
        await using var context = db.CreateDbContext();
        foreach (var name in SyntheticSnowballHistory.LoanNames)
        {
            context.Institutions.Add(new Institution
            {
                Name = name, AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime,
                Accounts =
                {
                    new Account
                    {
                        Name = name, Kind = AccountKind.Loan, CurrentBalance = 1000m,
                        Apr = withApr ? 0.05m : null, NextPaymentAmount = withApr ? 50m : null,
                    },
                },
            });
        }
        await context.SaveChangesAsync();
    }

    private static int SnapshotCount(TestDatabase db)
    {
        using var context = db.CreateDbContext();
        return context.SnowballSnapshots.Count();
    }

    // ---- the headline: a failed read must not lock the history out ----

    [Theory]
    [InlineData(SnowballHistoryStatus.Absent)]
    [InlineData(SnowballHistoryStatus.NeedsPassphrase)]
    [InlineData(SnowballHistoryStatus.WrongPassphrase)]
    [InlineData(SnowballHistoryStatus.Unreadable)]
    public async Task AFailedReadThenAWeeklySnapshotThenASuccessfulReadStillImports(SnowballHistoryStatus failure)
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db, withApr: true); // withApr: these count as Snowball loans, so the weekly row is written
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.Then(failure switch
        {
            SnowballHistoryStatus.Absent => SnowballHistoryReadResult.Absent,
            SnowballHistoryStatus.NeedsPassphrase => SnowballHistoryReadResult.NeedsPassphrase,
            SnowballHistoryStatus.WrongPassphrase => SnowballHistoryReadResult.WrongPassphrase,
            _ => SnowballHistoryReadResult.Unreadable("the network dropped"),
        });

        // The page's two calls, in the page's order.
        await harness.Service.ImportHistoryIfNeededAsync(default);
        await harness.Service.RecordCurrentWeekSnapshotAsync(default);
        Assert.Equal(10, SnapshotCount(db)); // the weekly rows only — no history yet
        Assert.False(harness.Memory.Imported);

        // Later the file is there (and, for a back-off, enough time has passed).
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(10 + 700, SnapshotCount(db));
        Assert.True(harness.Memory.Imported);
    }

    [Fact]
    public async Task TheWeeklyRowsAreNeverTheOnesThatCloseTheGate()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db, withApr: true);
        var harness = new HistoryImportHarness(db, Now);
        await harness.Service.RecordCurrentWeekSnapshotAsync(default);

        await using (var context = db.CreateDbContext())
        {
            Assert.All(context.SnowballSnapshots, s => Assert.True(s.WeekOf > SyntheticSnowballHistory.LastWeek));
        }

        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(10 + 700, SnapshotCount(db));
    }

    // ---- the gate itself ----

    [Fact]
    public async Task ASnapshotOnTheLastRecordedWeekMeansTheHistoryIsAlreadyHere()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        await using (var context = db.CreateDbContext())
        {
            var anyId = context.Accounts.First().Id;
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = anyId, WeekOf = SyntheticSnowballHistory.LastWeek, Balance = 5m });
            await context.SaveChangesAsync();
        }
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(1, SnapshotCount(db)); // nothing added
        Assert.True(harness.Memory.Imported);  // remembered, so Drive is not read again
    }

    [Fact]
    public async Task ASnapshotOneWeekAfterTheLastRecordedWeekLeavesTheGateOpen()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        await using (var context = db.CreateDbContext())
        {
            var anyId = context.Accounts.First().Id;
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = anyId, WeekOf = SyntheticSnowballHistory.LastWeek.AddDays(7), Balance = 5m });
            await context.SaveChangesAsync();
        }
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(1 + 700, SnapshotCount(db));
    }

    [Fact]
    public async Task HistoryAlreadyPresent_SetsTheFlagAfterOneReadAndNeverReadsAgain()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var first = new HistoryImportHarness(db, Now);
        await first.Service.ImportHistoryIfNeededAsync(default); // an earlier build's import: rows are there, flag absent
        var second = new HistoryImportHarness(db, Now);          // a new process: no flag yet

        await second.Service.ImportHistoryIfNeededAsync(default);
        await second.Service.ImportHistoryIfNeededAsync(default);
        await second.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(1, second.Source.Reads);
        Assert.True(second.Memory.Imported);
        Assert.Equal(700, SnapshotCount(db));
    }

    // ---- idempotence and the flag ----

    [Fact]
    public async Task ASuccessfulImportIsIdempotentAndTheFlagIsSetOnceItHasSucceeded()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(700, SnapshotCount(db));
        Assert.Equal(1, harness.Source.Reads); // the second open never asked Drive
        Assert.Equal(1, harness.Memory.MarkCalls);
    }

    [Theory]
    [InlineData(SnowballHistoryStatus.Absent)]
    [InlineData(SnowballHistoryStatus.NeedsPassphrase)]
    [InlineData(SnowballHistoryStatus.WrongPassphrase)]
    [InlineData(SnowballHistoryStatus.Unreadable)]
    public async Task TheFlagIsSetOnlyOnSuccess(SnowballHistoryStatus failure)
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.Then(failure switch
        {
            SnowballHistoryStatus.Absent => SnowballHistoryReadResult.Absent,
            SnowballHistoryStatus.NeedsPassphrase => SnowballHistoryReadResult.NeedsPassphrase,
            SnowballHistoryStatus.WrongPassphrase => SnowballHistoryReadResult.WrongPassphrase,
            _ => SnowballHistoryReadResult.Unreadable("could not tell"),
        });

        var exception = await Record.ExceptionAsync(() => harness.Service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        Assert.False(harness.Memory.Imported);
        Assert.Equal(0, harness.Memory.MarkCalls);
        Assert.Equal(0, SnapshotCount(db));
    }

    // ---- her install ----

    [Fact]
    public async Task HersInstallNeverReadsTheFile()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now, role: InstallRole.Hers);

        await harness.Service.ImportHistoryIfNeededAsync(default);
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(0, harness.Source.Reads);
        Assert.Equal(0, SnapshotCount(db));
        Assert.False(harness.Memory.Imported);
    }

    // ---- quiet failures ----

    [Fact]
    public async Task AWrongPassphraseIsQuietAndItsLogLineCarriesNeitherPassphraseNorPayload()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.Then(SnowballHistoryReadResult.WrongPassphrase);

        var exception = await Record.ExceptionAsync(() => harness.Service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        var line = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.DoesNotContain(HistoryImportHarness.Passphrase, line.Message, StringComparison.Ordinal);
        Assert.All(SyntheticSnowballHistory.LoanNames, name => Assert.DoesNotContain(name, line.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASourceThatThrowsIsLoggedAndNotRaisedIntoThePage()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.ThenThrow(new HttpRequestException("the network dropped"));

        var exception = await Record.ExceptionAsync(() => harness.Service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        Assert.False(harness.Memory.Imported);
        Assert.Contains(harness.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task CancellationStillCancels()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.ThenThrow(new OperationCanceledException());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Service.ImportHistoryIfNeededAsync(cancelled.Token));
    }

    private sealed class NeverAnswers : ISnowballHistorySource
    {
        public async Task<SnowballHistoryReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return SnowballHistoryReadResult.Absent;
        }
    }

    [Fact]
    public async Task ADriveCallThatNeverAnswersDoesNotHoldThePageOpen()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var memory = new FakeImportMemory();
        var logger = new RecordingLogger<SnowballService>();
        var service = new SnowballService(
            db, new FakeClock(Now),
            new SnowballHistoryImport(new NeverAnswers(), () => "pw", () => InstallRole.Owner, memory, readTimeout: TimeSpan.FromMilliseconds(50)),
            logger);

        var exception = await Record.ExceptionAsync(() => service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        Assert.False(memory.Imported);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WithNoHistorySourceConfiguredTheImportIsANoOp()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);

        await new SnowballService(db, new FakeClock(Now)).ImportHistoryIfNeededAsync(default);

        Assert.Equal(0, SnapshotCount(db));
    }

    [Fact]
    public async Task TheLoadedHistoryIsNotAcceptedForLoansThisInstallDoesNotHave()
    {
        using var db = new TestDatabase();
        var harness = new HistoryImportHarness(db, Now); // no accounts at all

        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(0, SnapshotCount(db));
        Assert.False(harness.Memory.Imported);
    }

    // ---- the back-off ----

    [Fact]
    public async Task AFlappingNetworkIsNotHitMoreThanOncePerPause()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.Then(SnowballHistoryReadResult.Unreadable("down")).Then(SnowballHistoryReadResult.Unreadable("down"));

        await harness.Service.ImportHistoryIfNeededAsync(default);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        await harness.Service.ImportHistoryIfNeededAsync(default);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(1, harness.Source.Reads);

        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(2, harness.Source.Reads);
    }

    [Fact]
    public async Task ChangingThePassphraseLiftsTheBackOffAtOnce()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);
        harness.Source.Then(SnowballHistoryReadResult.WrongPassphrase);

        await harness.Service.ImportHistoryIfNeededAsync(default);
        harness.CurrentPassphrase = "the-corrected-passphrase";
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(2, harness.Source.Reads);
        Assert.Equal([HistoryImportHarness.Passphrase, "the-corrected-passphrase"], harness.Source.Passphrases);
        Assert.True(harness.Memory.Imported);
    }

    [Fact]
    public async Task AMissingPassphraseIsNotBackedOffBecauseItCostsNoNetwork()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now, passphrase: null);
        harness.Source.Then(SnowballHistoryReadResult.NeedsPassphrase);

        await harness.Service.ImportHistoryIfNeededAsync(default);
        harness.CurrentPassphrase = HistoryImportHarness.Passphrase; // she types it in and comes straight back
        await harness.Service.ImportHistoryIfNeededAsync(default);

        Assert.Equal(2, harness.Source.Reads);
        Assert.True(harness.Memory.Imported);
    }
}
