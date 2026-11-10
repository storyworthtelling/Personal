using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §20, §37: a worker stopped mid-run left only its "started" line, and a worker with nothing to do returned
/// Success like one that worked, so the trail could not tell a skip, a run and a kill apart.
/// </summary>
public class WorkerTrailTests
{
    [Theory]
    [InlineData(3, "WORKER RefreshWorker stopped: reason 3 (timeout)")]
    [InlineData(7, "WORKER RefreshWorker stopped: reason 7 (no network)")]
    [InlineData(10, "WORKER RefreshWorker stopped: reason 10 (quota)")]
    [InlineData(99, "WORKER RefreshWorker stopped: reason 99")]
    public void AStopNamesItsReason(int reason, string line) =>
        Assert.Equal(line, WorkerTrail.StoppedLine("RefreshWorker", reason));

    [Fact]
    public void ASkipSaysWhy() =>
        Assert.Equal("WORKER LoopUploadWorker skipped: outside the upload window", WorkerTrail.SkippedLine("LoopUploadWorker", "outside the upload window"));
}
