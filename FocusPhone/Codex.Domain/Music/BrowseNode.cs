namespace Codex.Domain.Music;

/// <summary>
/// One node in the browsable content tree Android Auto / Assistant navigate — either a browsable folder or a
/// playable leaf carrying the same <c>drive://</c> uri the in-app player uses. Pure, so the tree logic is
/// unit-tested; the Android head maps a node to a Media3 <c>MediaItem</c>.
/// </summary>
public sealed record BrowseNode(string Id, string Title, bool IsPlayable, string? MediaUri, string? Subtitle = null);

/// <summary>
/// Builds the browse tree from the cached <see cref="MusicCatalog"/>: root → Albums / Artists / Songs →
/// playable tracks. The id scheme (and the unit-separator encoding for album keys) is the single source the
/// Android mapper and the Auto callback both read. Pure — no Media3 types.
/// </summary>
public sealed class MusicBrowseCatalog
{
    public const string RootId = "root";
    private const string AlbumsId = "albums";
    private const string ArtistsId = "artists";
    private const string SongsId = "songs";
    private const string AlbumPrefix = "album/";
    private const string ArtistPrefix = "artist/";
    private const char Separator = '\u001f';

    private readonly MusicCatalog catalog;

    public MusicBrowseCatalog(MusicCatalog catalog) => this.catalog = catalog;

    public BrowseNode Root() => Browsable(RootId, "Score");

    public IReadOnlyList<BrowseNode> Children(string parentId)
    {
        if (parentId == RootId)
        {
            return [Browsable(AlbumsId, "Albums"), Browsable(ArtistsId, "Artists"), Browsable(SongsId, "Songs")];
        }

        if (parentId == AlbumsId)
        {
            return [.. catalog.Albums().Select(album =>
                Browsable($"{AlbumPrefix}{album.AlbumArtist}{Separator}{album.Name}", album.Name))];
        }

        if (parentId == ArtistsId)
        {
            return [.. catalog.Artists().Select(artist => Browsable($"{ArtistPrefix}{artist.Name}", artist.Name))];
        }

        if (parentId == SongsId)
        {
            return [.. catalog.Songs().Select(Playable)];
        }

        if (parentId.StartsWith(AlbumPrefix, StringComparison.Ordinal))
        {
            var parts = parentId[AlbumPrefix.Length..].Split(Separator);
            return [.. catalog.TracksInAlbum(parts[0], parts.Length > 1 ? parts[1] : string.Empty).Select(Playable)];
        }

        if (parentId.StartsWith(ArtistPrefix, StringComparison.Ordinal))
        {
            return [.. catalog.TracksByArtist(parentId[ArtistPrefix.Length..]).Select(Playable)];
        }

        return [];
    }

    private static BrowseNode Browsable(string id, string title) => new(id, title, IsPlayable: false, MediaUri: null);

    private static BrowseNode Playable(Track track) =>
        new(track.Id, track.DisplayTitle, IsPlayable: true, MediaUri: $"drive://{track.Id}", Subtitle: track.DisplayArtist);
}
