using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class BrowseNodeTests
{
    private static Track Track(string id, string album, string albumArtist, string artist) =>
        new(id, "Title " + id, artist, album, albumArtist, 1, 1, "Jazz", TimeSpan.FromMinutes(3));

    private static MusicBrowseCatalog Catalog() => new(new MusicCatalog([
        Track("t1", "Blue", "Miles", "Miles"),
        Track("t2", "Blue", "Miles", "Miles"),
        Track("t3", "Red", "Bill", "Bill"),
    ]));

    [Fact]
    public void Root_IsBrowsable()
    {
        var root = Catalog().Root();
        Assert.Equal(MusicBrowseCatalog.RootId, root.Id);
        Assert.False(root.IsPlayable);
    }

    [Fact]
    public void RootChildren_AreTheThreeSections()
    {
        var titles = Catalog().Children(MusicBrowseCatalog.RootId).Select(n => n.Title).ToArray();
        Assert.Equal(["Albums", "Artists", "Songs"], titles);
    }

    [Fact]
    public void Albums_ExpandToAlbumNodes()
    {
        var albums = Catalog().Children("albums");
        Assert.Contains(albums, n => n.Title == "Blue" && !n.IsPlayable);
        Assert.Contains(albums, n => n.Title == "Red");
    }

    [Fact]
    public void Songs_ArePlayableWithDriveUris()
    {
        var songs = Catalog().Children("songs");
        Assert.Equal(3, songs.Count);
        Assert.All(songs, n => Assert.True(n.IsPlayable));
        Assert.All(songs, n => Assert.StartsWith("drive://", n.MediaUri));
    }

    [Fact]
    public void AlbumNode_ExpandsToItsTracks()
    {
        var albumNode = Catalog().Children("albums").First(n => n.Title == "Blue");
        var tracks = Catalog().Children(albumNode.Id);
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, n => Assert.True(n.IsPlayable));
    }

    [Fact]
    public void ArtistNode_ExpandsToItsTracks()
    {
        var tracks = Catalog().Children("artist/Bill");
        var track = Assert.Single(tracks);
        Assert.Equal("drive://t3", track.MediaUri);
    }

    [Fact]
    public void UnknownParent_IsEmpty()
    {
        Assert.Empty(Catalog().Children("nope/xyz"));
    }
}
