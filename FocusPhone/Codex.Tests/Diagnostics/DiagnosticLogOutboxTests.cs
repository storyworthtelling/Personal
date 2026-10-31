using Codex.Domain.Diagnostics;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

// Plan §3.1 (G8): with an outbox, a full DiagnosticLog seals instead of discarding its older half, and
// the Diagnostics screen reads live + sealed, so nothing leaves the phone until Drive has it.
public class DiagnosticLogOutboxTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "focusphone-outbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DiagnosticEntry Entry(string message) => new(
        new DateTimeOffset(2026, 10, 30, 9, 15, 0, TimeSpan.Zero), DiagnosticLevel.Warning, "Sync", message);

    private DiagnosticLog Log(long maxBytes = 200, string fileName = DiagnosticLog.FileName) =>
        new(directory, maxBytes, fileName, new LogOutbox(directory));

    [Fact]
    public void ARollSealsTheFullFileIntoTheOutboxRatherThanABackup()
    {
        var log = Log();
        for (var i = 0; i < 10; i++)
        {
            log.Append(Entry($"failure number {i:00} with enough words to fill the file"));
        }

        var outbox = new LogOutbox(directory);
        Assert.NotEmpty(outbox.SegmentsFor(DiagnosticLog.FileName));
        Assert.False(File.Exists(Path.Combine(directory, DiagnosticLog.FileName + ".1")));
    }

    [Fact]
    public void NothingIsDiscardedHoweverManyTimesItRolls()
    {
        var log = Log();
        for (var i = 0; i < 40; i++)
        {
            log.Append(Entry($"failure number {i:00} with enough words to fill the file"));
        }

        var all = log.ReadAll();
        for (var i = 0; i < 40; i++)
        {
            Assert.Contains($"failure number {i:00} ", all);
        }
    }

    [Fact]
    public void TheScreenReadsSealedThenLiveOldestFirst()
    {
        var log = Log();
        log.Append(Entry("first, long enough to need a roll on the next append"));
        log.Append(Entry("second, long enough to need a roll on the next append"));
        log.Append(Entry("third"));

        var all = log.ReadAll();
        Assert.True(all.IndexOf("first", StringComparison.Ordinal) < all.IndexOf("second", StringComparison.Ordinal));
        Assert.True(all.IndexOf("second", StringComparison.Ordinal) < all.IndexOf("third", StringComparison.Ordinal));
        Assert.True(log.HasEntries);
    }

    [Fact]
    public void HasEntriesStaysTrueWhileOnlySealedSegmentsRemain()
    {
        var log = Log();
        log.Append(Entry("something"));
        new LogOutbox(directory).SealAll([log.LivePath]);

        Assert.True(log.HasEntries);
        Assert.Contains("something", log.ReadAll());
    }

    [Fact]
    public void ClearTakesTheLogsSealedSegmentsTooButNotAnotherLogs()
    {
        var failures = Log();
        var texts = Log(fileName: "texts.log");
        failures.Append(Entry("failure"));
        texts.Append(Entry("text"));
        new LogOutbox(directory).SealAll([failures.LivePath, texts.LivePath]);
        failures.Append(Entry("live failure"));

        failures.Clear();

        Assert.False(failures.HasEntries);
        Assert.Equal(string.Empty, failures.ReadAll());
        Assert.Contains("text", texts.ReadAll());
    }

    [Fact]
    public void RestoreAfterClearBringsTheContentBack()
    {
        var log = Log();
        log.Append(Entry("first, long enough to need a roll on the next append"));
        log.Append(Entry("second"));
        var snapshot = log.ReadAll();

        log.Clear();
        log.RestoreRaw(snapshot);

        Assert.Equal(snapshot, log.ReadAll());
    }

    [Fact]
    public void ABackupLeftByTheOldRollIsSealedRatherThanLostOnUpgrade()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, DiagnosticLog.FileName + ".1"), "from before the upgrade\n");

        var log = Log(maxBytes: 128 * 1024);
        log.Append(Entry("after the upgrade"));

        Assert.False(File.Exists(Path.Combine(directory, DiagnosticLog.FileName + ".1")));
        var all = log.ReadAll();
        Assert.True(all.IndexOf("from before the upgrade", StringComparison.Ordinal)
            < all.IndexOf("after the upgrade", StringComparison.Ordinal));
        Assert.Single(new LogOutbox(directory).SegmentsFor(DiagnosticLog.FileName));
    }

    [Fact]
    public void WithoutAnOutboxTheOldRollIsUnchanged()
    {
        var log = new DiagnosticLog(directory, 200);
        for (var i = 0; i < 10; i++)
        {
            log.Append(Entry($"failure number {i:00} with enough words to fill the file"));
        }

        Assert.True(File.Exists(Path.Combine(directory, DiagnosticLog.FileName + ".1")));
        Assert.False(Directory.Exists(Path.Combine(directory, LogOutbox.DirectoryName)));
    }

    [Fact]
    public void TheLivePathIsWhatTheUploadWorkerSeals()
    {
        Assert.Equal(Path.Combine(directory, "texts.log"), Log(fileName: "texts.log").LivePath);
    }
}
