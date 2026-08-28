namespace Codex.Domain.Music;

/// <summary>
/// One music track, built from a file's embedded tags. <see cref="Id"/> is the
/// Google Drive file id (also the media id the queue and progress book key on).
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
    int Bpm = 0)
{
    public string DisplayTitle => Blank(Title) ? "Unknown Title" : Title;

    public string DisplayArtist => Blank(ArtistName) ? "Unknown Artist" : ArtistName;

    public string DisplayAlbum => Blank(AlbumName) ? "Unknown Album" : AlbumName;

    /// <summary>Album artist keeps compilations together; falls back to the track artist.</summary>
    public string DisplayAlbumArtist => Blank(AlbumArtist) ? DisplayArtist : AlbumArtist;

    public string DisplayGenre => Blank(Genre) ? "Unknown Genre" : Genre;

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
