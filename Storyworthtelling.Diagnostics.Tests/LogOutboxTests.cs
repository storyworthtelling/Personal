using Storyworthtelling.Diagnostics.Tests.Support;

namespace Storyworthtelling.Diagnostics.Tests;

// The outbox is where a full log goes instead of the bin (plan §3.1, G8): a roll seals the file under a
// monotonic sequence number, the upload worker seals every live log at once, and the cap drops the oldest
// segment with a DROPPED line so the reader knows the record has a hole.
public class LogOutboxTests
{
    [Fact]
    public void SealingMovesTheLiveFileIntoTheOutboxUnderTheNextSequence()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        File.WriteAllText(live, "first line\n");
        var outbox = new LogOutbox(folder.Path);

        var segment = outbox.Seal(live);

        Assert.NotNull(segment);
        Assert.False(File.Exists(live));
        Assert.Equal(1, segment!.Sequence);
        Assert.Equal("diagnostic.log", segment.LogName);
        Assert.Equal("000001.diagnostic.log.sealed", Path.GetFileName(segment.FilePath));
        Assert.Equal(Path.Combine(folder.Path, LogOutbox.DirectoryName), Path.GetDirectoryName(segment.FilePath));
        Assert.Equal("first line\n", outbox.ReadText(segment));
    }

    [Fact]
    public void ARollSealsInsteadOfDiscarding_EverySealedSegmentIsKept()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path);

        for (var i = 1; i <= 3; i++)
        {
            File.WriteAllText(live, $"roll {i}\n");
            outbox.Seal(live);
        }

        Assert.Equal(new long[] { 1, 2, 3 }, outbox.Segments().Select(s => s.Sequence));
        Assert.Equal("roll 1\nroll 2\nroll 3\n", outbox.ReadSealed("diagnostic.log"));
    }

    [Fact]
    public void AMissingOrEmptyLiveFileSealsNothing()
    {
        using var folder = new TempFolder();
        var outbox = new LogOutbox(folder.Path);
        File.WriteAllText(folder.File("empty.log"), string.Empty);

        Assert.Null(outbox.Seal(folder.File("absent.log")));
        Assert.Null(outbox.Seal(folder.File("empty.log")));
        Assert.Empty(outbox.Segments());
    }

    [Fact]
    public void SealAllTakesEveryLiveLogInOnePassAndAWriterStartsAFreshFile()
    {
        using var folder = new TempFolder();
        var failures = folder.File("diagnostic.log");
        var texts = folder.File("texts.log");
        File.WriteAllText(failures, "failure\n");
        File.WriteAllText(texts, "text\n");
        var outbox = new LogOutbox(folder.Path);

        var sealedNow = outbox.SealAll([failures, texts]);

        Assert.Equal(2, sealedNow.Count);
        Assert.False(File.Exists(failures));
        Assert.False(File.Exists(texts));

        // A writer that appends after the cut simply creates a new live file; nothing it writes lands in
        // a segment that is already on its way to Drive.
        File.AppendAllText(failures, "after the cut\n");
        Assert.Equal("failure\n", outbox.ReadSealed("diagnostic.log"));
        Assert.Equal("after the cut\n", File.ReadAllText(failures));
    }

    [Fact]
    public void ReadSealedKeepsEachLogToItself()
    {
        using var folder = new TempFolder();
        var outbox = new LogOutbox(folder.Path);
        File.WriteAllText(folder.File("diagnostic.log"), "failure\n");
        outbox.Seal(folder.File("diagnostic.log"));
        File.WriteAllText(folder.File("texts.log"), "text\n");
        outbox.Seal(folder.File("texts.log"));

        Assert.Equal("failure\n", outbox.ReadSealed("diagnostic.log"));
        Assert.Equal("text\n", outbox.ReadSealed("texts.log"));
        Assert.Single(outbox.SegmentsFor("texts.log"));
    }

    [Fact]
    public void TheSequenceSurvivesARestart()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        File.WriteAllText(live, "before\n");
        new LogOutbox(folder.Path).Seal(live);

        // A new process: a new instance over the same folder.
        var restarted = new LogOutbox(folder.Path);
        File.WriteAllText(live, "after\n");
        var segment = restarted.Seal(live);

        Assert.Equal(2, segment!.Sequence);
        Assert.Equal(2, restarted.Segments().Count);
    }

    [Fact]
    public void TheSequenceNeverReusesANumberEvenWhenTheCounterFileIsLost()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path);
        File.WriteAllText(live, "one\n");
        outbox.Seal(live);
        File.WriteAllText(live, "two\n");
        outbox.Seal(live);
        File.Delete(Path.Combine(outbox.Directory, LogOutbox.SequenceFileName));

        File.WriteAllText(live, "three\n");
        var segment = new LogOutbox(folder.Path).Seal(live);

        Assert.Equal(3, segment!.Sequence);
    }

    [Fact]
    public void PastTheCapTheOldestIsDroppedAndTheRecordSaysSo()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path, capBytes: 250);
        var payload = new string('x', 99) + "\n"; // 100 bytes per segment

        for (var i = 0; i < 4; i++)
        {
            File.WriteAllText(live, payload);
            outbox.Seal(live);
        }

        var remaining = outbox.Segments();
        Assert.Equal(new long[] { 3, 4 }, remaining.Select(s => s.Sequence));
        var opening = outbox.ReadText(remaining[0]).Split('\n')[0];
        Assert.StartsWith("DROPPED 2 segments (oldest ", opening);
        Assert.EndsWith(")", opening);
    }

    [Fact]
    public void ASecondDropAddsToTheCountRatherThanForgettingTheFirst()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path, capBytes: 250);
        var payload = new string('y', 99) + "\n";

        for (var i = 0; i < 6; i++)
        {
            File.WriteAllText(live, payload);
            outbox.Seal(live);
        }

        var remaining = outbox.Segments();
        Assert.Equal(new long[] { 5, 6 }, remaining.Select(s => s.Sequence));
        Assert.StartsWith("DROPPED 4 segments (oldest ", outbox.ReadText(remaining[0]));
    }

    [Fact]
    public void TheNewestSegmentIsAlwaysKeptEvenWhenItAloneExceedsTheCap()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path, capBytes: 10);
        File.WriteAllText(live, new string('z', 50));

        outbox.Seal(live);

        Assert.Single(outbox.Segments());
    }

    [Fact]
    public void DeleteRemovesExactlyTheNamedSegments()
    {
        using var folder = new TempFolder();
        var live = folder.File("diagnostic.log");
        var outbox = new LogOutbox(folder.Path);
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(live, $"segment {i}\n");
            outbox.Seal(live);
        }
        var all = outbox.Segments();

        outbox.Delete([all[0], all[2]]);

        Assert.Equal(new long[] { 2 }, outbox.Segments().Select(s => s.Sequence));
    }

    [Fact]
    public void ClearLogRemovesOnlyThatLogsSegments()
    {
        using var folder = new TempFolder();
        var outbox = new LogOutbox(folder.Path);
        File.WriteAllText(folder.File("diagnostic.log"), "failure\n");
        outbox.Seal(folder.File("diagnostic.log"));
        File.WriteAllText(folder.File("texts.log"), "text\n");
        outbox.Seal(folder.File("texts.log"));

        outbox.ClearLog("diagnostic.log");

        Assert.Empty(outbox.SegmentsFor("diagnostic.log"));
        Assert.Single(outbox.SegmentsFor("texts.log"));
    }

    [Fact]
    public void StrayFilesInTheOutboxAreIgnored()
    {
        using var folder = new TempFolder();
        var outbox = new LogOutbox(folder.Path);
        Directory.CreateDirectory(outbox.Directory);
        File.WriteAllText(Path.Combine(outbox.Directory, "notes.txt"), "not a segment");
        File.WriteAllText(Path.Combine(outbox.Directory, "abc.diagnostic.log.sealed"), "bad number");

        Assert.Empty(outbox.Segments());
    }
}
