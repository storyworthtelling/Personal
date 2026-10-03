using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Snowball;
using Index.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>
/// The Snowball page used to <c>await</c> the history import before it painted anything, so a Drive that
/// answered slowly (or not at all, up to the 30-second read cap) left the owner on a blank page. The page now
/// paints from local data FIRST and runs the import in the background, redrawing in place only if rows were
/// written. These tests pin that order against a source that is held shut until the test lets it go.
/// </summary>
public class SnowballOpeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>A source whose read does not answer until the test releases it.</summary>
    private sealed class HeldSource : ISnowballHistorySource
    {
        private readonly TaskCompletionSource<SnowballHistoryReadResult> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasAsked { get; private set; }

        public void Release() => gate.TrySetResult(FakeSnowballHistorySource.SyntheticLoaded());

        public Task<SnowballHistoryReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken)
        {
            WasAsked = true;
            return gate.Task;
        }
    }

    private static async Task SeedAllTenAsync(TestDatabase db)
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
                        Apr = 0.05m, NextPaymentAmount = 50m,
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

    [Fact(Timeout = 20000)]
    public async Task ASlowSourceDoesNotDelayTheFirstPaint()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var held = new HeldSource();
        var logger = new RecordingLogger<SnowballOpeningTests>();
        var service = new Index.Infrastructure.Services.SnowballService(
            db, new FakeClock(Now),
            new SnowballHistoryImport(held, () => "pw", () => InstallRole.Owner, new FakeImportMemory()),
            new RecordingLogger<Index.Infrastructure.Services.SnowballService>());
        var paints = 0;
        var repaints = 0;

        var opening = SnowballOpening.OpenAsync(
            service.RecordCurrentWeekSnapshotAsync, service.ImportHistoryIfNeededAsync,
            paint: () => { paints++; return Task.CompletedTask; },
            repaint: () => { repaints++; return Task.CompletedTask; },
            logger, CancellationToken.None);

        var finished = await Task.WhenAny(opening, Task.Delay(Patience));
        Assert.True(ReferenceEquals(finished, opening), "OpenAsync waited for the held source before returning.");
        var background = await opening;
        Assert.Equal(1, paints);
        Assert.Equal(0, repaints);
        Assert.False(background.IsCompleted, "the import should still be waiting on the held source");

        held.Release();
        await background.WaitAsync(Patience);

        Assert.Equal(1, paints);
        Assert.Equal(1, repaints);
        Assert.True(SnapshotCount(db) > 0);
    }

    [Fact(Timeout = 20000)]
    public async Task TheWeeklySnapshotIsStillRecordedBeforeThePaint()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var held = new HeldSource();
        var service = new Index.Infrastructure.Services.SnowballService(
            db, new FakeClock(Now),
            new SnowballHistoryImport(held, () => "pw", () => InstallRole.Owner, new FakeImportMemory()),
            new RecordingLogger<Index.Infrastructure.Services.SnowballService>());
        var snapshotsAtPaint = -1;

        var background = await SnowballOpening.OpenAsync(
            service.RecordCurrentWeekSnapshotAsync, service.ImportHistoryIfNeededAsync,
            paint: () => { snapshotsAtPaint = SnapshotCount(db); return Task.CompletedTask; },
            repaint: () => Task.CompletedTask,
            new RecordingLogger<SnowballOpeningTests>(), CancellationToken.None);

        Assert.True(snapshotsAtPaint > 0, "this week's snapshot must exist by the time the page paints");
        held.Release();
        await background.WaitAsync(Patience);
    }

    [Fact(Timeout = 20000)]
    public async Task AnImportThatWritesNothingDoesNotRedraw()
    {
        var repaints = 0;

        var background = await SnowballOpening.OpenAsync(
            _ => Task.CompletedTask, _ => Task.FromResult(false),
            paint: () => Task.CompletedTask,
            repaint: () => { repaints++; return Task.CompletedTask; },
            new RecordingLogger<SnowballOpeningTests>(), CancellationToken.None);
        await background.WaitAsync(Patience);

        Assert.Equal(0, repaints);
    }

    [Fact(Timeout = 20000)]
    public async Task AnImportThatThrowsIsLoggedAndNeverReachesThePage()
    {
        var logger = new RecordingLogger<SnowballOpeningTests>();
        var repaints = 0;

        var background = await SnowballOpening.OpenAsync(
            _ => Task.CompletedTask, _ => throw new InvalidOperationException("the history source fell over"),
            paint: () => Task.CompletedTask,
            repaint: () => { repaints++; return Task.CompletedTask; },
            logger, CancellationToken.None);
        var exception = await Record.ExceptionAsync(() => background.WaitAsync(Patience));

        Assert.Null(exception);
        Assert.Equal(0, repaints);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("fell over", entry.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 20000)]
    public async Task ARedrawThatThrowsIsLoggedToo()
    {
        var logger = new RecordingLogger<SnowballOpeningTests>();

        var background = await SnowballOpening.OpenAsync(
            _ => Task.CompletedTask, _ => Task.FromResult(true),
            paint: () => Task.CompletedTask,
            repaint: () => throw new InvalidOperationException("the page was gone"),
            logger, CancellationToken.None);
        var exception = await Record.ExceptionAsync(() => background.WaitAsync(Patience));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("page was gone", StringComparison.Ordinal));
    }

    [Fact(Timeout = 20000)]
    public async Task APageThatClosesWhileTheImportWaitsEndsQuietly()
    {
        var logger = new RecordingLogger<SnowballOpeningTests>();
        var repaints = 0;
        using var closed = new CancellationTokenSource();

        var background = await SnowballOpening.OpenAsync(
            _ => Task.CompletedTask,
            async token => { await Task.Delay(Timeout.Infinite, token); return true; },
            paint: () => Task.CompletedTask,
            repaint: () => { repaints++; return Task.CompletedTask; },
            logger, closed.Token);
        await closed.CancelAsync();
        var exception = await Record.ExceptionAsync(() => background.WaitAsync(Patience));

        Assert.Null(exception);
        Assert.Equal(0, repaints);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact(Timeout = 20000)]
    public async Task TheFirstPaintComesBeforeTheImportIsEvenAsked()
    {
        var order = new List<string>();

        var background = await SnowballOpening.OpenAsync(
            _ => { order.Add("record"); return Task.CompletedTask; },
            _ => { order.Add("import"); return Task.FromResult(false); },
            paint: () => { order.Add("paint"); return Task.CompletedTask; },
            repaint: () => Task.CompletedTask,
            new RecordingLogger<SnowballOpeningTests>(), CancellationToken.None);
        await background.WaitAsync(Patience);

        Assert.Equal(["record", "paint", "import"], order);
    }
}
