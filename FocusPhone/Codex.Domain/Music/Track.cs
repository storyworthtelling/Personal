namespace Codex.Domain.Music;

/// <summary>
/// Where a track can be played from.
///
/// The app is not supposed to show this. Owned files and streamed tracks are
/// deliberately indistinguishable — one library, one queue, one listening log — and
/// the only state that ever reaches a screen is <see cref="Unavailable"/>, which is
/// drawn as DESIGN.md §8's Disabled row rather than removed.
/// </summary>
public enum TrackAvailability
{
    /// <summary>A file the owner has. The durable library, and the one that wins.</summary>
    Local,

    /// <summary>Carried by the streaming provider.</summary>
    Streaming,

    /// <summary>Was streamable and is not any more — pulled, or the region changed.</summary>
    Unavailable,
}

/// <summary>
/// One music track, built from a file's embedded tags. <see cref="Id"/> is the
/// Google Drive file id (also the media id the queue and progress book key on) —
/// unless this is a hidden-track split segment, in which case <see cref="Id"/> is a
/// per-segment id of its own (each half needs its own queue/progress identity) and
/// <see cref="SourceFileId"/> names the file both halves actually share.
/// The Display* members apply the fallbacks (Unknown Artist/Album/Genre, album
/// artist defaulting to the track artist) in one place so the catalog, shuffle,
/// and smart-mix all group identically.
///
/// <see cref="Bpm"/> is 0 when nothing has established a tempo for the file. Zero is
/// "unknown", not "no beats" — a workout pool has to be able to tell the difference
/// between a track that is the wrong speed and one nobody has measured.
/// </summary>
public record Track(
    string Id,
    string Title,
    string ArtistName,
    string AlbumName,
    string AlbumArtist,
    int TrackNumber,
    int DiscNumber,
    string Genre,
    TimeSpan Duration,
    string? ArtRef = null,
    int Bpm = 0,
    TrackAvailability Availability = TrackAvailability.Local,
    string? SourceFileId = null,
    TimeSpan? ClipStart = null)
{
    /// <summary>Whether this can actually be started. False only for a track the
    /// provider has stopped carrying.</summary>
    public bool IsPlayable => Availability != TrackAvailability.Unavailable;

    /// <summary>The id to actually resolve and stream — <see cref="Id"/> itself, unless
    /// this is a hidden-track split segment sharing another track's file.</summary>
    public string ResolvedSourceId => SourceFileId ?? Id;

    public string DisplayTitle => Blank(Title) ? "Unknown Title" : Title;

    public string DisplayArtist => Blank(ArtistName) ? "Unknown Artist" : ArtistName;

    public string DisplayAlbum => Blank(AlbumName) ? "Unknown Album" : AlbumName;

    /// <summary>Album artist keeps compilations together; falls back to the track artist.</summary>
    public string DisplayAlbumArtist => Blank(AlbumArtist) ? DisplayArtist : AlbumArtist;

    public string DisplayGenre => Blank(Genre) ? "Unknown Genre" : Genre;

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
