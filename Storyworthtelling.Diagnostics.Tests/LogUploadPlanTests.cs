namespace Storyworthtelling.Diagnostics.Tests;

// The upload's decisions (plan §3.3), pure so "when is it safe to delete a log" exists once. The worker
// only carries them out: no key stops everything; a name that already holds the same content is cleared
// without a second create; a name holding something else gets an .r2, never an overwrite; and only a
// batch whose read-back matched is cleared.
public class LogUploadPlanTests
{
    private const string App = "post";
    private const string InstallId = "a1b2c3d4e5f6";
    private const string LocalSha = "aaaa";

    private static SealedSegment Segment(long sequence, long bytes, string log = "diagnostic.log") =>
        new(sequence, log, $"/outbox/{sequence:000000}.{log}.sealed", bytes,
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero).AddMinutes(sequence));

    [Fact]
    public void NoKeyUploadsAndClearsNothing()
    {
        var run = LogUploadPlan.Decide([Segment(1, 100)], LogKeyState.Missing, App, InstallId);

        Assert.Empty(run.Batches);
        Assert.Equal(LogUploadRefusal.WaitingForPassphrase, run.Refusal);
        Assert.Equal("Logs are waiting for the vault passphrase.", LogUploadStatus.Sentence(run.Refusal, 0));
    }

    [Fact]
    public void NothingSealedIsNothingToDo()
    {
        var run = LogUploadPlan.Decide([], LogKeyState.Ready, App, InstallId);

        Assert.Empty(run.Batches);
        Assert.Null(run.Refusal);
    }

    [Fact]
    public void BatchesAreOldestFirstAndHoldAtMost256KilobytesOfPlaintext()
    {
        var segments = new[] { Segment(3, 128 * 1024), Segment(1, 128 * 1024), Segment(2, 100 * 1024), Segment(4, 10) };

        var run = LogUploadPlan.Decide(segments, LogKeyState.Ready, App, InstallId);

        Assert.Equal(2, run.Batches.Count);
        Assert.Equal(new long[] { 1, 2 }, run.Batches[0].Segments.Select(s => s.Sequence));
        Assert.Equal(new long[] { 3, 4 }, run.Batches[1].Segments.Select(s => s.Sequence));
        Assert.All(run.Batches, b => Assert.True(b.Segments.Sum(s => s.Bytes) <= LogBatch.MaxPlaintextBytes));
    }

    [Fact]
    public void ASegmentLargerThanTheBatchLimitTravelsAlone()
    {
        var run = LogUploadPlan.Decide([Segment(1, 300 * 1024), Segment(2, 10)], LogKeyState.Ready, App, InstallId);

        Assert.Equal(2, run.Batches.Count);
        Assert.Single(run.Batches[0].Segments);
    }

    [Fact]
    public void ARunTakesAtMostFortyBatchesSoALongTripDrainsOverDays()
    {
        var segments = Enumerable.Range(1, 100).Select(i => Segment(i, 200 * 1024)).ToArray();

        var run = LogUploadPlan.Decide(segments, LogKeyState.Ready, App, InstallId);

        Assert.Equal(LogUploadPlan.MaxBatchesPerRun, run.Batches.Count);
        Assert.Equal(40, LogUploadPlan.MaxBatchesPerRun);
        Assert.Equal(1, run.Batches[0].Segments[0].Sequence);
        Assert.Equal(40, run.Batches[^1].Segments[0].Sequence);
    }

    [Fact]
    public void TheNameCarriesAppInstallAndSegmentRange()
    {
        var run = LogUploadPlan.Decide([Segment(7, 10), Segment(9, 10)], LogKeyState.Ready, App, InstallId);

        Assert.Equal("swt-log.post.a1b2c3d4e5f6.000007-000009.json", run.Batches[0].Name);
        Assert.Equal("storyworthtelling-logs", LogUploadPlan.FolderName);
    }

    [Fact]
    public void AFreeNameIsCreated()
    {
        var action = LogUploadPlan.ForBatch("swt-log.post.x.000001-000001.json", LocalSha, []);

        Assert.Equal(BatchActionKind.Create, action.Kind);
        Assert.Equal("swt-log.post.x.000001-000001.json", action.Name);
    }

    [Fact]
    public void TheSameNameWithTheSameContentSkipsTheCreateAndClears()
    {
        const string name = "swt-log.post.x.000001-000001.json";

        var action = LogUploadPlan.ForBatch(name, LocalSha, [new RemoteCopy(name, LocalSha)]);

        Assert.Equal(BatchActionKind.AlreadyUploaded, action.Kind);
        Assert.Equal(name, action.Name);
        Assert.True(action.ClearsWithoutReadBack);
    }

    [Fact]
    public void TheSameNameWithDifferentContentWritesR2NeverOverwrites()
    {
        const string name = "swt-log.post.x.000001-000001.json";

        var action = LogUploadPlan.ForBatch(name, LocalSha, [new RemoteCopy(name, "bbbb")]);

        Assert.Equal(BatchActionKind.Create, action.Kind);
        Assert.Equal("swt-log.post.x.000001-000001.r2.json", action.Name);
    }

    [Fact]
    public void AnUnreadableCopyCountsAsDifferent()
    {
        const string name = "swt-log.post.x.000001-000001.json";

        var action = LogUploadPlan.ForBatch(name, LocalSha, [new RemoteCopy(name, null)]);

        Assert.Equal("swt-log.post.x.000001-000001.r2.json", action.Name);
    }

    [Fact]
    public void AMatchingRevisionIsFoundAndTheNextFreeRevisionIsUsedOtherwise()
    {
        const string name = "swt-log.post.x.000001-000001.json";
        var r2 = LogUploadPlan.RevisionName(name, 2);
        var r3 = LogUploadPlan.RevisionName(name, 3);

        var matched = LogUploadPlan.ForBatch(name, LocalSha, [new RemoteCopy(name, "b"), new RemoteCopy(r2, LocalSha)]);
        var next = LogUploadPlan.ForBatch(name, LocalSha, [new RemoteCopy(name, "b"), new RemoteCopy(r2, "c")]);

        Assert.Equal(BatchActionKind.AlreadyUploaded, matched.Kind);
        Assert.Equal(r2, matched.Name);
        Assert.Equal(BatchActionKind.Create, next.Kind);
        Assert.Equal(r3, next.Name);
        Assert.Equal("swt-log.post.x.000001-000001.r3.json", r3);
    }

    [Fact]
    public void AReadBackMismatchKeepsEverything()
    {
        Assert.False(LogUploadPlan.Confirmed(LocalSha, "something else"));
        Assert.False(LogUploadPlan.Confirmed(LocalSha, null));
        Assert.True(LogUploadPlan.Confirmed(LocalSha, LocalSha));
    }

    [Fact]
    public void OnlyTheConfirmedBatchesSegmentsAreCleared()
    {
        var run = LogUploadPlan.Decide(
            [Segment(1, 200 * 1024), Segment(2, 200 * 1024), Segment(3, 200 * 1024)],
            LogKeyState.Ready, App, InstallId);

        var outcomes = new[]
        {
            new BatchOutcome(run.Batches[0], Confirmed: true),
            new BatchOutcome(run.Batches[1], Confirmed: false),
            new BatchOutcome(run.Batches[2], Confirmed: true),
        };

        var cleared = LogUploadPlan.ToClear(outcomes);

        Assert.Equal(new long[] { 1, 3 }, cleared.Select(s => s.Sequence));
    }

    [Fact]
    public void ThreeFailedWeeksInARowSaySoAndFewerStayQuiet()
    {
        Assert.Null(LogUploadStatus.Sentence(null, 0));
        Assert.Null(LogUploadStatus.Sentence(null, 2));
        Assert.Equal("Logs have not reached Drive for 3 weeks.", LogUploadStatus.Sentence(null, 3));
        Assert.Equal("Logs have not reached Drive for 5 weeks.", LogUploadStatus.Sentence(null, 5));
    }
}
