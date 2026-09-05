using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class MusicListeningRecorderTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

    private static Track T(string id, string genre = "Jazz") =>
        new(id, "Title " + id, "Bill Evans", "Album", "Bill Evans", 1, 1, genre, TimeSpan.FromMinutes(4));

    private static MusicListeningRecorder Recorder() => new(() => At);

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void AutoAdvance_NearEnd_RecordsCompletedAndAlbumLaunch()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1"), T("t2")], 0, PlaybackLaunch.Album, _ => false);
        recorder.Observe("t1", Min(3.9), Min(4));           // played almost to the end
        var events = recorder.Observe("t2", TimeSpan.Zero, Min(4)); // advanced -> finalize t1

        var signals = events.Select(e => e.Signal).ToList();
        Assert.Contains(ListeningSignal.PlayedFromAlbum, signals);
        Assert.Contains(ListeningSignal.Completed, signals);
        Assert.All(events, e => Assert.Equal("t1", e.TrackId));
        Assert.All(events, e => Assert.Equal(20, e.LocalHour));
    }

    [Fact]
    public void EarlySwitch_RecordsSkipped()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1"), T("t2")], 0, PlaybackLaunch.Album, _ => false);
        recorder.Observe("t1", Min(0.3), Min(4));
        var events = recorder.Observe("t2", TimeSpan.Zero, Min(4));

        Assert.Contains(ListeningSignal.Skipped, events.Select(e => e.Signal));
        Assert.DoesNotContain(ListeningSignal.Completed, events.Select(e => e.Signal));
    }

    [Fact]
    public void ShuffleLaunch_PartialListen_RecordsOnlyLaunch()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1")], 0, PlaybackLaunch.Shuffle, _ => false);
        recorder.Observe("t1", Min(2), Min(4));
        var events = recorder.Stop();

        var signals = events.Select(e => e.Signal).ToList();
        Assert.Contains(ListeningSignal.PlayedFromShuffle, signals);
        Assert.DoesNotContain(ListeningSignal.Completed, signals);
        Assert.DoesNotContain(ListeningSignal.Skipped, signals);
    }

    [Fact]
    public void ReplayOfFinishedTrack_RecordsReplayed()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1")], 0, PlaybackLaunch.Album, _ => true); // already complete -> replay
        recorder.Observe("t1", Min(2), Min(4));
        var events = recorder.Stop();

        Assert.Contains(ListeningSignal.Replayed, events.Select(e => e.Signal));
    }

    [Fact]
    public void LearningDisabled_RecordsNothing()
    {
        var recorder = Recorder();
        recorder.LearningEnabled = false;
        recorder.BeginSelection([T("t1"), T("t2")], 0, PlaybackLaunch.Album, _ => false);
        recorder.Observe("t1", Min(3.9), Min(4));
        Assert.Empty(recorder.Observe("t2", TimeSpan.Zero, Min(4)));
        Assert.Empty(recorder.Stop());
    }

    [Fact]
    public void BeginningANewSelection_FinalizesTheInFlightTrack()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1")], 0, PlaybackLaunch.Album, _ => false);
        recorder.Observe("t1", Min(3.95), Min(4));
        var events = recorder.BeginSelection([T("t2")], 0, PlaybackLaunch.Shuffle, _ => false);

        Assert.Contains(ListeningSignal.Completed, events.Select(e => e.Signal));
        Assert.All(events, e => Assert.Equal("t1", e.TrackId));
    }

    [Fact]
    public void EventsInOneSelectionShareASession()
    {
        var recorder = Recorder();
        recorder.BeginSelection([T("t1"), T("t2")], 0, PlaybackLaunch.Album, _ => false);
        recorder.Observe("t1", Min(3.9), Min(4));
        var first = recorder.Observe("t2", TimeSpan.Zero, Min(4));
        recorder.Observe("t2", Min(3.9), Min(4));
        var second = recorder.Stop();

        Assert.Equal(first[0].SessionId, second[0].SessionId);
    }
}
