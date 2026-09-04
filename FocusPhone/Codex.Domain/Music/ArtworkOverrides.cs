namespace Codex.Domain.Music;

/// <summary>
/// The owner's custom album art: a map from album → a local image path, used when the embedded art is
/// missing or ugly. Resolution precedence (override → embedded <see cref="Track.ArtRef"/> → default) lives
/// where art is drawn; this owns the override map and the stable album key. Pure.
/// </summary>
public sealed class ArtworkOverrides
{
    private const char Separator = '\u001f';

    private readonly Dictionary<string, string> byAlbum;

    public ArtworkOverrides(IReadOnlyDictionary<string, string>? saved = null) =>
        byAlbum = saved is null ? [] : new Dictionary<string, string>(saved);

    /// <summary>The album grouping key — album artist + album, matching how the catalog groups albums.</summary>
    public static string AlbumKey(Track track) => AlbumKey(track.DisplayAlbumArtist, track.DisplayAlbum);

    /// <summary>The album key from raw tags, for callers (like the now-playing screen) holding metadata, not a Track.</summary>
    public static string AlbumKey(string albumArtist, string album) => $"{albumArtist}{Separator}{album}";

    public string? Resolve(string albumKey) => byAlbum.TryGetValue(albumKey, out var path) ? path : null;

    public void Set(string albumKey, string imagePath) => byAlbum[albumKey] = imagePath;

    public void Clear(string albumKey) => byAlbum.Remove(albumKey);

    public IReadOnlyDictionary<string, string> Export() => new Dictionary<string, string>(byAlbum);
}
