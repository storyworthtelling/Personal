namespace FocusPhone.Domain.Music;

/// <summary>
/// One completed play episode of a track — the facts the Android recorder observes when playback
/// leaves a track, handed to <see cref="SignalDeriver"/> to turn into <see cref="ListeningEvent"/>s.
/// </summary>
/// <param name="PlayedDuration">How much of the track was actually listened to this episode.</param>
/// <param name="TrackDuration">The track's full length (may be <see cref="TimeSpan.Zero"/> if unknown).</param>
/// <param name="ReachedEnd">Playback ran off the end of the track.</param>
/// <param name="WasAlreadyComplete">The track had already been finished before this play — a replay.</param>
public sealed record TrackPlay(
    Track Track,
    PlaybackLaunch Launch,
    TimeSpan PlayedDuration,
    TimeSpan TrackDuration,
    bool ReachedEnd,
    bool WasAlreadyComplete,
    int LocalHour,
    string SessionId,
    DateTimeOffset AtUtc);
