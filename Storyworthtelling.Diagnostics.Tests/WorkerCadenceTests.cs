namespace Storyworthtelling.Diagnostics.Tests;

// G7: a background worker that silently never runs shows up only as a gap in its expected cadence.
// WorkerTrail writes the start/end lines; WorkerCadence.Missed finds the gaps.
public class WorkerCadenceTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

    [Fact]
    public void ARegularWorkerHasNoGaps()
    {
        var runs = Enumerable.Range(0, 12).Select(i => Midnight.AddMinutes(30 * i)).ToList();

        var gaps = WorkerCadence.Missed(runs, HalfHour, Midnight, Midnight.AddHours(6));

        Assert.Empty(gaps);
    }

    [Fact]
    public void ASilentStretchLongerThanTheCadenceAndItsGraceIsAGap()
    {
        var runs = new[] { Midnight, Midnight.AddMinutes(30), Midnight.AddHours(4), Midnight.AddHours(4.5) };

        var gap = Assert.Single(WorkerCadence.Missed(runs, HalfHour, Midnight, Midnight.AddHours(5)));

        Assert.Equal(Midnight.AddMinutes(30), gap.From);
        Assert.Equal(Midnight.AddHours(4), gap.To);
        Assert.Equal(6, gap.MissedRuns);
    }

    [Fact]
    public void WorkManagerFlexIsNotAGap()
    {
        // Periodic work drifts: 45 minutes between two half-hourly runs is within the grace.
        var runs = new[] { Midnight, Midnight.AddMinutes(45), Midnight.AddMinutes(75) };

        Assert.Empty(WorkerCadence.Missed(runs, HalfHour, Midnight, Midnight.AddMinutes(90)));
    }

    [Fact]
    public void AWorkerThatNeverRanIsOneGapFromTheStartOfTheWindow()
    {
        var gap = Assert.Single(WorkerCadence.Missed([], TimeSpan.FromDays(1), Midnight, Midnight.AddDays(7)));

        Assert.Equal(Midnight, gap.From);
        Assert.Equal(Midnight.AddDays(7), gap.To);
    }

    [Fact]
    public void TheTrailingSilenceUpToNowCounts()
    {
        var runs = new[] { Midnight };

        var gap = Assert.Single(WorkerCadence.Missed(runs, TimeSpan.FromDays(1), Midnight, Midnight.AddDays(3)));

        Assert.Equal(Midnight, gap.From);
        Assert.Equal(Midnight.AddDays(3), gap.To);
    }

    [Fact]
    public void TheTrailLinesNameTheWorkerResultAndDuration()
    {
        Assert.Equal("WORKER EmailSyncWorker started", WorkerTrail.StartedLine("EmailSyncWorker"));
        Assert.Equal(
            "WORKER EmailSyncWorker finished: Success in 1.25 s",
            WorkerTrail.FinishedLine("EmailSyncWorker", "Success", TimeSpan.FromMilliseconds(1_250)));
        Assert.Equal(
            "WORKER LogUploadWorker threw after 0.50 s: System.IO.IOException",
            WorkerTrail.ThrewLine("LogUploadWorker", TimeSpan.FromMilliseconds(500), new IOException("disk")));
    }

    [Fact]
    public void TheLaunchLineTiesEveryLaterLineToABuild()
    {
        Assert.Equal(
            "LAUNCH Post 1.0.42 (200042) on Android 15 (API 35)",
            LaunchLine.Format("Post", "1.0.42", 200042, "15", 35));
    }
}
