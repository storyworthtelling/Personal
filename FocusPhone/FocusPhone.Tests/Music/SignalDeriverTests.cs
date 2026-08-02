using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class SignalDeriverTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

    private static Track TrackWith(string genre = "Jazz", string artist = "Bill Evans", string albumArtist = "Bill Evans") =>
        new("id1", "Waltz for Debby", artist, "Waltz for Debby", albumArtist, 1, 1, genre, TimeSpan.FromMinutes(4));

    private static TrackPlay Play(
        PlaybackLaunch launch = PlaybackLaunch.Album,
        double playedFraction = 1.0,
        bool reachedEnd = false,
        bool wasAlreadyComplete = false,
        Track? track = null,
        int localHour = 20,
        string sessionId = "s1")
    {
        var t = track ?? TrackWith();
        return new TrackPlay(
            t, launch,
            PlayedDuration: t.Duration * playedFraction,
            TrackDuration: t.Duration,
            ReachedEnd: reachedEnd,
            WasAlreadyComplete: wasAlreadyComplete,
            LocalHour: localHour,
            SessionId: sessionId,
            AtUtc: Now);
    }

    [Fact]
    public void Derive_LearningDisabled_EmitsNothing()
    {
        Assert.Empty(SignalDeriver.Derive(Play(), learningEnabled: false));
    }

    [Fact]
    public void Derive_AlbumLaunch_EmitsPlayedFromAlbum()
    {
        var signals = SignalDeriver.Derive(Play(PlaybackLaunch.Album, playedFraction: 0.5), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.PlayedFromAlbum, signals);
        Assert.DoesNotContain(ListeningSignal.PlayedFromShuffle, signals);
    }

    [Fact]
    public void Derive_ShuffleLaunch_EmitsPlayedFromShuffle()
    {
        var signals = SignalDeriver.Derive(Play(PlaybackLaunch.Shuffle, playedFraction: 0.5), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.PlayedFromShuffle, signals);
        Assert.DoesNotContain(ListeningSignal.PlayedFromAlbum, signals);
    }

    [Fact]
    public void Derive_ReachedEnd_EmitsCompleted()
    {
        var signals = SignalDeriver.Derive(Play(reachedEnd: true, playedFraction: 0.4), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.Completed, signals);
        Assert.DoesNotContain(ListeningSignal.Skipped, signals);
    }

    [Fact]
    public void Derive_NearEnd_EmitsCompletedWithoutReachingEnd()
    {
        var signals = SignalDeriver.Derive(Play(playedFraction: 0.95), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.Completed, signals);
    }

    [Fact]
    public void Derive_EarlyExit_EmitsSkipped()
    {
        var signals = SignalDeriver.Derive(Play(playedFraction: 0.1), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.Skipped, signals);
        Assert.DoesNotContain(ListeningSignal.Completed, signals);
    }

    [Fact]
    public void Derive_PartialListen_EmitsNoOutcomeSignal()
    {
        var signals = SignalDeriver.Derive(Play(playedFraction: 0.5), learningEnabled: true)
            .Select(e => e.Signal)
            .ToList();
        Assert.DoesNotContain(ListeningSignal.Completed, signals);
        Assert.DoesNotContain(ListeningSignal.Skipped, signals);
        Assert.Contains(ListeningSignal.PlayedFromAlbum, signals); // launch still recorded
    }

    [Fact]
    public void Derive_ReplayOfFinishedTrack_EmitsReplayed()
    {
        var signals = SignalDeriver.Derive(Play(wasAlreadyComplete: true, playedFraction: 0.5), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.Contains(ListeningSignal.Replayed, signals);
    }

    [Fact]
    public void Derive_ReplayWithNoPlayback_DoesNotEmitReplayed()
    {
        var signals = SignalDeriver.Derive(Play(wasAlreadyComplete: true, playedFraction: 0.0), learningEnabled: true)
            .Select(e => e.Signal);
        Assert.DoesNotContain(ListeningSignal.Replayed, signals);
    }

    [Fact]
    public void Derive_EventCarriesTrackTagsAndContext()
    {
        var e = SignalDeriver.Derive(Play(localHour: 7, sessionId: "sX"), learningEnabled: true)[0];
        Assert.Equal("id1", e.TrackId);
        Assert.Equal("Jazz", e.Genre);
        Assert.Equal("Bill Evans", e.ArtistName);
        Assert.Equal("Bill Evans", e.AlbumArtist);
        Assert.Equal(7, e.LocalHour);
        Assert.Equal("sX", e.SessionId);
    }

    [Fact]
    public void Derive_BlankTags_UseDisplayFallbacks()
    {
        var track = new Track("id2", "T", "", "A", "", 1, 1, "", TimeSpan.FromMinutes(3));
        var e = SignalDeriver.Derive(Play(track: track), learningEnabled: true)[0];
        Assert.Equal("Unknown Genre", e.Genre);
        Assert.Equal("Unknown Artist", e.ArtistName);
        Assert.Equal("Unknown Artist", e.AlbumArtist); // album artist falls back to track artist
    }

    [Fact]
    public void Derive_ZeroDuration_DoesNotEmitSkip()
    {
        var track = new Track("id3", "T", "A", "Al", "A", 1, 1, "Rock", TimeSpan.Zero);
        var play = new TrackPlay(track, PlaybackLaunch.Shuffle, TimeSpan.Zero, TimeSpan.Zero, false, false, 12, "s1", Now);
        var signals = SignalDeriver.Derive(play, learningEnabled: true).Select(e => e.Signal);
        Assert.DoesNotContain(ListeningSignal.Skipped, signals);
        Assert.DoesNotContain(ListeningSignal.Completed, signals);
    }
}
