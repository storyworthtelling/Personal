using AndroidX.Media3.Common;
using FocusPhone.Domain.Music;

namespace FocusPhone.Audio;

/// <summary>
/// The Android adapter over the pure <see cref="MusicBrowseCatalog"/>: maps its <see cref="BrowseNode"/>s to
/// Media3 <see cref="MediaItem"/>s for the Android Auto / Assistant browse tree. All the tree logic (ids,
/// expansion, album-key encoding) is unit-tested in the Domain; this file only builds MediaItems, so it
/// stays a thin, mechanical mapping. Playable leaves carry the same drive:// uri the in-app player uses.
/// </summary>
public sealed class MediaBrowseTree
{
    public const string RootId = MusicBrowseCatalog.RootId;

    private readonly MusicBrowseCatalog catalog;

    public MediaBrowseTree(MusicCatalog catalog) => this.catalog = new MusicBrowseCatalog(catalog);

    public MediaItem Root() => ToItem(catalog.Root());

    public IList<MediaItem> Children(string parentId) => [.. catalog.Children(parentId).Select(ToItem)];

    private static MediaItem ToItem(BrowseNode node) => node.IsPlayable ? Playable(node) : Browsable(node);

    private static MediaItem Browsable(BrowseNode node)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(node.Title)!
            .SetIsBrowsable(Java.Lang.Boolean.True)!
            .SetIsPlayable(Java.Lang.Boolean.False)!
            .Build()!;
        return new MediaItem.Builder().SetMediaId(node.Id)!.SetMediaMetadata(metadata)!.Build()!;
    }

    private static MediaItem Playable(BrowseNode node)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(node.Title)!
            .SetArtist(node.Subtitle)!
            .SetIsBrowsable(Java.Lang.Boolean.False)!
            .SetIsPlayable(Java.Lang.Boolean.True)!
            .Build()!;
        return new MediaItem.Builder()
            .SetMediaId(node.Id)!
            .SetUri(node.MediaUri)!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }
}
