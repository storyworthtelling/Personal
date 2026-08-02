namespace FocusPhone.Domain.Music;

/// <summary>
/// A single passive observation of how the owner treated a track — the raw material the
/// Taste Profile is reduced from. Passive-only by design: no ratings, thumbs, or stars.
/// </summary>
public enum ListeningSignal
{
    /// <summary>Played to (near) the end — the strongest positive signal.</summary>
    Completed,

    /// <summary>Left early after barely starting — a negative signal.</summary>
    Skipped,

    /// <summary>Re-played a track that was already finished — a strong affinity signal.</summary>
    Replayed,

    /// <summary>Started from an album/artist/genre list — chosen deliberately.</summary>
    PlayedFromAlbum,

    /// <summary>Started from shuffle — surfaced by chance, not sought out.</summary>
    PlayedFromShuffle,
}

/// <summary>How a track's play began — deliberate list vs shuffle — for the album-vs-shuffle lean.</summary>
public enum PlaybackLaunch
{
    Album,
    Shuffle,
}
