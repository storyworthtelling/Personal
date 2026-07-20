using FocusPhone.Domain.Diagnostics;
using Xunit;

namespace FocusPhone.Tests.Diagnostics;

public class DiagnosticLogTests : IDisposable
{
    private readonly string directory;

    public DiagnosticLogTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "focusphone-diaglog-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DiagnosticEntry Entry(string message) => new(
        new DateTimeOffset(2026, 7, 10, 9, 15, 0, TimeSpan.Zero),
        DiagnosticLevel.Warning,
        "EmailSync",
        message);

    [Fact]
    public void ReadAll_EmptyDirectory_ReturnsEmpty()
    {
        var log = new DiagnosticLog(directory);

        Assert.False(log.HasEntries);
        Assert.Equal(string.Empty, log.ReadAll());
    }

    [Fact]
    public void Append_ThenReadAll_ContainsEntry()
    {
        var log = new DiagnosticLog(directory);

        log.Append(Entry("Email sync failed"));

        Assert.True(log.HasEntries);
        Assert.Contains("Email sync failed", log.ReadAll());
    }

    [Fact]
    public void Append_MultipleEntries_AllPresentInOrder()
    {
        var log = new DiagnosticLog(directory);

        log.Append(Entry("first"));
        log.Append(Entry("second"));

        var text = log.ReadAll();
        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void Append_PastCap_StitchesBackupAndLiveInReadAll()
    {
        // Tiny cap so a couple of entries force a roll without writing 128 KB.
        var log = new DiagnosticLog(directory, maxBytes: 200);
        var backupPath = Path.Combine(directory, DiagnosticLog.FileName + ".1");

        log.Append(Entry("charter entry before the roll"));
        // Append filler until exactly the first roll happens: the entry that
        // triggers the roll lands in the fresh live file, everything before is
        // moved to the backup.
        var filler = 0;
        while (!File.Exists(backupPath) && filler < 200)
        {
            filler++;
            log.Append(Entry($"filler entry {filler} padding out the log file"));
        }

        Assert.True(File.Exists(backupPath), "a backup file should exist once the live file rolled");
        var text = log.ReadAll();
        Assert.Contains("charter entry before the roll", text); // from the backup
        Assert.Contains($"filler entry {filler} ", text);        // newest, in live
    }

    [Fact]
    public void Append_ManyRolls_DropsOldestButKeepsNewest()
    {
        var log = new DiagnosticLog(directory, maxBytes: 200);

        log.Append(Entry("generation one should eventually fall off"));
        // Enough entries to roll several times, discarding the first backup.
        for (var i = 0; i < 60; i++)
        {
            log.Append(Entry($"filler entry number {i} padding out the log file"));
        }
        log.Append(Entry("latest generation survivor"));

        var text = log.ReadAll();
        Assert.DoesNotContain("generation one should eventually fall off", text);
        Assert.Contains("latest generation survivor", text);
    }

    [Fact]
    public void Append_ManyEntries_StaysBounded()
    {
        const long cap = 200;
        var log = new DiagnosticLog(directory, maxBytes: cap);

        for (var i = 0; i < 500; i++)
        {
            log.Append(Entry($"filler entry number {i} padding out the log file"));
        }

        // Live + one backup keeps growth to roughly twice the cap, nowhere near
        // the ~45 KB an unrolled log of 500 entries would reach.
        Assert.True(log.ReadAll().Length <= 4 * cap, "rolling should bound the log size");
        Assert.Contains("filler entry number 499", log.ReadAll());
    }

    [Fact]
    public void Clear_RemovesLiveAndBackup()
    {
        var log = new DiagnosticLog(directory, maxBytes: 200);
        for (var i = 0; i < 12; i++)
        {
            log.Append(Entry($"filler entry number {i} padding out the log file"));
        }
        Assert.True(File.Exists(Path.Combine(directory, DiagnosticLog.FileName + ".1")));

        log.Clear();

        Assert.False(log.HasEntries);
        Assert.False(File.Exists(Path.Combine(directory, DiagnosticLog.FileName)));
        Assert.False(File.Exists(Path.Combine(directory, DiagnosticLog.FileName + ".1")));
    }

    [Fact]
    public void Clear_WhenEmpty_DoesNotThrow()
    {
        var log = new DiagnosticLog(directory);

        log.Clear();

        Assert.False(log.HasEntries);
    }

    [Fact]
    public void RestoreRaw_AfterClear_BringsBackContent()
    {
        var log = new DiagnosticLog(directory);
        log.Append(Entry("a failure worth undoing"));
        var snapshot = log.ReadAll();

        log.Clear();
        Assert.False(log.HasEntries);
        log.RestoreRaw(snapshot);

        Assert.True(log.HasEntries);
        Assert.Contains("a failure worth undoing", log.ReadAll());
    }
}
