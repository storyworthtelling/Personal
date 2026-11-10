using Storyworthtelling.Diagnostics;

namespace LogLoopReader.Tests;

/// <summary>
/// Audit 08 §1–5, §34–35: what the digest could not see. Every name and number is invented.
/// </summary>
public class DigestHealthTests
{
    // A line exactly as the MAUI heads' FileLogger wrote it before this change.
    private static string MauiLine(string level, string source, string message, string? detail = null) =>
        $"[2026-10-30 09:15:00] [{level}] {source}: {message}" + (detail is null ? string.Empty : "\n       " + detail) + "\n";

    [Fact]
    public void TheOldMauiLineShapeIsReadAsEntries()
    {
        var entries = LogEntries.Parse(
            MauiLine("WARN", "ReviewProcessor", "Manuscript 7 failed") + MauiLine("INFO", "Worker", "WORKER PipelineWorker started"));

        Assert.Equal(2, entries.Count);
        Assert.Equal("WARN", entries[0].Level);
        Assert.Equal("ReviewProcessor", entries[0].Source);
        Assert.Equal("Manuscript 7 failed", entries[0].Message);
        Assert.Equal("Worker", entries[1].Source);
    }

    [Fact]
    public void TheFileLoggersOwnOutputIsReadAsEntries()
    {
        // The reader is fed the shape the MAUI file logger writes through (LogLine), not a hand-made imitation of it.
        var text = LogLine.Format(new DateTime(2026, 10, 30, 9, 15, 0), "WARN", "ReviewProcessor", "Manuscript 7 failed", null);

        var entry = Assert.Single(LogEntries.Parse(text));

        Assert.Equal("WARN", entry.Level);
        Assert.Equal("ReviewProcessor", entry.Source);
        Assert.Equal("Manuscript 7 failed", entry.Message);
    }

    [Fact]
    public void AGroupCarriesTheBuildsItWasSeenOnAndItsOwnTimes()
    {
        var early = Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed"), day: 30) with { };
        var later = new LogBatch(early.Header with { VersionCode = 200043, AppVersion = "1.0.43" }, early.Parts);

        var group = Assert.Single(Digest.Build([early, later]));

        Assert.Equal([200042L, 200043L], group.Versions);
        // The entry's own stamp, not the batch's range.
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 9, 15, 0, TimeSpan.Zero), group.LastSeen);
    }

    [Fact]
    public void TheDigestPutsACrashAboveAThousandRoutineLines()
    {
        var routine = string.Concat(Enumerable.Repeat(Support.Entry("INFO", "Launch", "LAUNCH Post 1.0.42 (200042)"), 1000));
        var crash = Support.Entry("CRASH", "Post", "Unhandled exception", "System.InvalidOperationException: x\n   at Post.A.B()");

        var rendered = Digest.Render(
            Digest.Build([Support.Batch("post", "owner", routine + crash)]),
            new LogExcerptSanitizer(new HashSet<string>(StringComparer.Ordinal) { "Post", "Launch" }));

        Assert.True(rendered.IndexOf("exception", StringComparison.Ordinal) < rendered.IndexOf("1000×", StringComparison.Ordinal));
    }

    [Fact]
    public void DroppedSegmentsAreTheFirstThingTheDigestSays()
    {
        var batches = new[] { Support.Batch("folio", "owner", Support.Entry("WARN", "Sync", "failed") + "DROPPED 3 segments (oldest 2026-10-20T08:00:00Z)\n") };

        var rendered = Digest.Render(Digest.Build(batches), new LogExcerptSanitizer(new HashSet<string>(StringComparer.Ordinal)), LoopHealth.From(batches));

        var dropped = rendered.IndexOf("DROPPED", StringComparison.Ordinal);
        Assert.True(dropped >= 0 && dropped < rendered.IndexOf("## folio", StringComparison.Ordinal));
        Assert.Contains("folio (owner)", rendered);
    }

    [Fact]
    public void AWorkerThatStoppedRunningIsNamed()
    {
        // Folio's pipeline runs every 15 minutes; one run at 08:00 and silence until 20:00 is a worker that stopped.
        var batches = new[] { Support.Batch("folio", "owner", Support.Entry("INFO", "Worker", "WORKER PipelineWorker started")) };

        var health = LoopHealth.From(batches);

        var gap = health.MissedWorkers.Single(m => m.Worker == "PipelineWorker" && m.To == new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal("folio", gap.App);
        Assert.Equal("owner", gap.Phone);
        Assert.True(gap.MissedRuns > 40);
    }

    [Fact]
    public void AWorkerOnScheduleIsNotNamed()
    {
        var day = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var lines = string.Concat(Enumerable.Range(0, 3).Select(i =>
            $"[{day.AddHours(8 + i * 6):MMM d, h:mm tt}] INFO Worker — WORKER UpdateWorker started\n\n"));
        var batches = new[] { Support.Batch("flyleaf", "owner", lines) };

        Assert.DoesNotContain(LoopHealth.From(batches).MissedWorkers, m => m.Worker == "UpdateWorker");
    }

    [Fact]
    public void AnAppAPhoneShouldSendAndDidNotIsNamed()
    {
        var batches = new[] { Support.Batch("post", "owner", Support.Entry("INFO", "Launch", "LAUNCH")) };

        var health = LoopHealth.From(batches);

        Assert.Contains(("score", "owner"), health.Silent);
        Assert.Contains(("constitution", "partner"), health.Silent);
        Assert.DoesNotContain(("post", "owner"), health.Silent);
        Assert.DoesNotContain(("post", "partner"), health.Silent);   // Post runs on the locked phone only
    }

    [Fact]
    public void AJavaOnlyStackStillGroupsByItsFrames()
    {
        const string one = "Java.Lang.IllegalStateException: x\n   at Android.Media.MediaPlayer.Start()\n   at Java.Interop.Native()";
        const string two = "Java.Lang.IllegalStateException: x\n   at Android.Hardware.Camera.Open()\n   at Java.Interop.Native()";

        var groups = Digest.Build([Support.Batch("score", "owner", Support.Entry("WARN", "Player", "failed", one) + Support.Entry("WARN", "Player", "failed", two))]);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Key.Contains("Android.Media.MediaPlayer.Start", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExceptionWrittenIntoTheMessageKeepsItsType()
    {
        // Score once logged "… threw: {e}", so the type sat in the message and the detail opened on a frame.
        var entry = Support.Entry("WARN", "Taste", "Refine threw: System.Net.Http.HttpRequestException: 503", "   at Score.Taste.Refine()");

        var group = Assert.Single(Digest.Build([Support.Batch("score", "owner", entry)]));

        Assert.StartsWith("System.Net.Http.HttpRequestException @ ", group.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTrailedWorkerHasACadence()
    {
        // A worker wrapped in the trail but missing from the table could stop running and nobody would be told.
        var trailed = Directory.EnumerateFiles(Support.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(path), @"(?:HeadDiagnostics|MauiLogLoop|PostDiagnostics)\.Worker\(nameof\((?<name>\w+)\)"))
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var listed = LoopHealth.ExpectedWorkers.Values.SelectMany(workers => workers.Select(w => w.Worker)).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(trailed);
        Assert.Empty(trailed.Except(listed));
    }

    [Fact]
    public void ARunThatStartedAndNeverEndedIsNamed()
    {
        // WorkManager's ten-minute cap, or a process killed mid-run: a start with no finish, threw or stopped after it.
        var lines =
            "[2026-10-30 08:00:00] INFO Worker — WORKER RefreshWorker started\n\n" +
            "[2026-10-30 08:01:00] INFO Worker — WORKER RefreshWorker finished: Success in 60.00 s\n\n" +
            "[2026-10-30 12:00:00] INFO Worker — WORKER RefreshWorker started\n\n" +
            "[2026-10-30 16:00:00] INFO Worker — WORKER RefreshWorker started\n\n" +
            "[2026-10-30 16:02:00] INFO Worker — WORKER RefreshWorker stopped: reason 3 (timeout)\n\n";

        var health = LoopHealth.From([Support.Batch("index", "owner", lines)]);

        var unfinished = Assert.Single(health.Unfinished);
        Assert.Equal(("index", "owner", "RefreshWorker", 1), (unfinished.App, unfinished.Phone, unfinished.Worker, unfinished.Count));
    }

    [Fact]
    public void AnIsoStampIsReadToTheMillisecondInItsZone()
    {
        var header = Support.Batch("post", "owner", string.Empty).Header;

        Assert.Equal(
            new DateTimeOffset(2026, 10, 30, 13, 15, 5, 250, TimeSpan.Zero),
            EntryTime.Read("2026-10-30T09:15:05.250-04:00", header));
    }
}
