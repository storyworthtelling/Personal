using AndroidX.Media3.Common;
using FocusPhone.Domain.Music;

namespace FocusPhone.Audio;

/// <summary>
/// The browsable content tree Android Auto / Assistant navigate: root → Albums /
/// Artists / Songs → playable tracks (drive:// uris). Built from the cached
/// <see cref="MusicCatalog"/>. Playable leaves carry the same drive:// uri the
/// in-app player uses, so the resolving data source streams them the same way.
/// </summary>
public sealed class MediaBrowseTree
{
    public const string RootId = "root";
    private const string AlbumsId = "albums";
    private const string ArtistsId = "artists";
    private const string SongsId = "songs";
    private const char Separator = '\u001f';

    private readonly MusicCatalog catalog;

    public MediaBrowseTree(MusicCatalog catalog) => this.catalog = catalog;

    public MediaItem Root() => Browsable(RootId, "Audio");

    public IList<MediaItem> Children(string parentId)
    {
        if (parentId == RootId)
        {
            return [Browsable(AlbumsId, "Albums"), Browsable(ArtistsId, "Artists"), Browsable(SongsId, "Songs")];
        }

        if (parentId == AlbumsId)
        {
            return [.. catalog.Albums().Select(album =>
                Browsable($"album/{album.AlbumArtist}{Separator}{album.Name}", album.Name))];
        }

        if (parentId == ArtistsId)
        {
            return [.. catalog.Artists().Select(artist => Browsable($"artist/{artist.Name}", artist.Name))];
        }

        if (parentId == SongsId)
        {
            return [.. catalog.Songs().Select(Playable)];
        }

        if (parentId.StartsWith("album/", StringComparison.Ordinal))
        {
            var parts = parentId["album/".Length..].Split(Separator);
            return [.. catalog.TracksInAlbum(parts[0], parts.Length > 1 ? parts[1] : string.Empty).Select(Playable)];
        }

        if (parentId.StartsWith("artist/", StringComparison.Ordinal))
        {
            return [.. catalog.TracksByArtist(parentId["artist/".Length..]).Select(Playable)];
        }

        return [];
    }

    private static MediaItem Browsable(string id, string title)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(title)!
            .SetIsBrowsable(true)!
            .SetIsPlayable(false)!
            .Build()!;
        return new MediaItem.Builder().SetMediaId(id)!.SetMediaMetadata(metadata)!.Build()!;
    }

    private static MediaItem Playable(Track track)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(track.DisplayTitle)!
            .SetArtist(track.DisplayArtist)!
            .SetAlbumTitle(track.DisplayAlbum)!
            .SetIsBrowsable(false)!
            .SetIsPlayable(true)!
            .Build()!;
        return new MediaItem.Builder()
            .SetMediaId(track.Id)!
            .SetUri($"drive://{track.Id}")!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }
}
