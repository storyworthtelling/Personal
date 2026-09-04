namespace Codex.Domain.Music;

/// <summary>
/// Turns one <see cref="TrackPlay"/> into the passive <see cref="ListeningEvent"/>s it warrants —
/// the "map" half of the Taste Profile. A play yields exactly one launch signal (album vs shuffle),
/// optionally a replay signal, and optionally one outcome signal (completed or skipped). A partial
/// listen that is neither near the end nor an early bail records only the launch. The
/// <c>learningEnabled</c> gate is the kill switch: off, nothing is ever derived.
/// </summary>
public static class SignalDeriver
{
    /// <summary>Fraction of the track that counts as "finished" when playback didn't run off the end.</summary>
    public const double CompletionFraction = 0.9;

    /// <summary>Below this fraction, leaving the track reads as a skip.</summary>
    public const double SkipFraction = 0.2;

    public static IReadOnlyList<ListeningEvent> Derive(TrackPlay play, bool learningEnabled)
    {
        ArgumentNullException.ThrowIfNull(play);
        if (!learningEnabled)
        {
            return [];
        }

        var track = play.Track;
        var events = new List<ListeningEvent>();

        ListeningEvent Event(ListeningSignal signal) => new(
            track.Id, track.DisplayGenre, track.DisplayArtist, track.DisplayAlbumArtist,
            signal, play.LocalHour, play.SessionId, play.AtUtc);

        events.Add(Event(play.Launch == PlaybackLaunch.Album
            ? ListeningSignal.PlayedFromAlbum
            : ListeningSignal.PlayedFromShuffle));

        if (play.WasAlreadyComplete && play.PlayedDuration > TimeSpan.Zero)
        {
            events.Add(Event(ListeningSignal.Replayed));
        }

        var fraction = play.TrackDuration > TimeSpan.Zero
            ? play.PlayedDuration.TotalSeconds / play.TrackDuration.TotalSeconds
            : 0.0;
        if (play.ReachedEnd || fraction >= CompletionFraction)
        {
            events.Add(Event(ListeningSignal.Completed));
        }
        else if (play.TrackDuration > TimeSpan.Zero && fraction < SkipFraction)
        {
            events.Add(Event(ListeningSignal.Skipped));
        }

        return events;
    }
}
