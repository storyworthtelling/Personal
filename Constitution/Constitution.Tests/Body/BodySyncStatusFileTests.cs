using Constitution.Domain.Body;
using Constitution.Domain.Diagnostics;
using Constitution.Infrastructure.Body;

namespace Constitution.Tests.Body;

/// <summary>The last sync, remembered as shapes for Diagnostics: an outcome, a gap kind and a time, and nothing from the body.</summary>
public class BodySyncStatusFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "constitution-sync-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Moment = new(2026, 11, 17, 7, 30, 0, TimeSpan.Zero);

    private string FilePath => Path.Combine(directory, "body-sync.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static BodySyncResult Result(BodySyncGap gap, params SignalReadEntry[] entries) =>
        new(BodySyncTrigger.AppOpen, true, gap, new SignalReadReport(entries));

    [Fact]
    public void NeverRanBeforeAnythingIsRecorded()
    {
        var file = new BodySyncStatusFile(FilePath, () => Moment);

        Assert.Equal(DiagnosticsOutcome.NeverRan, file.LastOutcome());
        Assert.Null(file.Last());
    }

    [Fact]
    public void RecordsTheOutcomeTheGapAndTheTime()
    {
        var file = new BodySyncStatusFile(FilePath, () => Moment);

        file.Record(Result(BodySyncGap.BackgroundNotGranted, new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Read, 3)));

        var last = new BodySyncStatusFile(FilePath, () => Moment).Last()!;
        Assert.Equal(DiagnosticsOutcome.Partial, last.Outcome);
        Assert.Equal(BodySyncGap.BackgroundNotGranted, last.Gap);
        Assert.Equal(BodySyncTrigger.AppOpen, last.Trigger);
        Assert.Equal(Moment, last.At);
    }

    [Fact]
    public void ALaterSyncReplacesTheEarlier()
    {
        var file = new BodySyncStatusFile(FilePath, () => Moment);
        file.Record(Result(BodySyncGap.BackgroundNotGranted, new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Read, 1)));
        file.Record(Result(BodySyncGap.None, new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Read, 1)));

        Assert.Equal(DiagnosticsOutcome.Succeeded, file.LastOutcome());
    }

    [Fact]
    public void AGarbledFileReadsAsNeverRan()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "][ nonsense");

        Assert.Equal(DiagnosticsOutcome.NeverRan, new BodySyncStatusFile(FilePath, () => Moment).LastOutcome());
    }

    [Fact]
    public void TheFileCarriesNoSignalAndNoCount()
    {
        var file = new BodySyncStatusFile(FilePath, () => Moment);
        file.Record(Result(BodySyncGap.None, new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Read, 41)));

        var text = File.ReadAllText(FilePath);

        Assert.DoesNotContain("weight", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("41", text, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory).Where(path => path != FilePath));
    }
}
