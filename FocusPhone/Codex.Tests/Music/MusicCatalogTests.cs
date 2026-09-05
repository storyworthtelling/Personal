using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class MusicCatalogTests
{
    private static Track Tr(
        string id,
        string title,
        string artist,
        string album,
        string albumArtist = "",
        int track = 1,
        int disc = 1,
        string genre = "Rock") =>
        new(id, title, artist, album, albumArtist, track, disc, genre, TimeSpan.FromMinutes(3));

    [Fact]
    public void Songs_AreAlphabeticalByTitle()
    {
        var catalog = new MusicCatalog([Tr("1", "Zebra", "X", "A"), Tr("2", "Apple", "Y", "B")]);

        Assert.Equal(["Apple", "Zebra"], catalog.Songs().Select(track => track.DisplayTitle).ToArray());
    }

    [Fact]
    public void Albums_GroupTracksAndOrderByDiscThenTrack()
    {
        var catalog = new MusicCatalog(
        [
            Tr("1", "Second", "Band", "Record", track: 2, disc: 1),
            Tr("2", "First", "Band", "Record", track: 1, disc: 1),
            Tr("3", "DiscTwo", "Band", "Record", track: 1, disc: 2),
        ]);

        var album = Assert.Single(catalog.Albums());
        Assert.Equal("Record", album.Name);
        Assert.Equal(["First", "Second", "DiscTwo"], album.Tracks.Select(track => track.DisplayTitle).ToArray());
    }

    [Fact]
    public void Albums_KeepCompilationTogetherUnderAlbumArtist()
    {
        // Different track artists but one album artist => one album.
        var catalog = new MusicCatalog(
        [
            Tr("1", "T1", "Artist One", "Mix", albumArtist: "Various Artists", track: 1),
            Tr("2", "T2", "Artist Two", "Mix", albumArtist: "Various Artists", track: 2),
        ]);

        var album = Assert.Single(catalog.Albums());
        Assert.Equal("Various Artists", album.AlbumArtist);
        Assert.Equal(2, album.Tracks.Count);
    }

    [Fact]
    public void Artists_ListedByTrackArtist()
    {
        var catalog = new MusicCatalog([Tr("1", "T1", "Beta", "A"), Tr("2", "T2", "Alpha", "B")]);

        Assert.Equal(["Alpha", "Beta"], catalog.Artists().Select(artist => artist.Name).ToArray());
    }

    [Fact]
    public void MissingTags_FallBackToUnknownBuckets()
    {
        var catalog = new MusicCatalog([Tr("1", "T1", "", "", genre: "")]);

        Assert.Equal("Unknown Artist", Assert.Single(catalog.Artists()).Name);
        Assert.Equal("Unknown Album", Assert.Single(catalog.Albums()).Name);
        Assert.Equal("Unknown Genre", Assert.Single(catalog.Genres()));
    }

    [Fact]
    public void TracksByArtist_ReturnsOnlyThatArtistInAlbumOrder()
    {
        var catalog = new MusicCatalog(
        [
            Tr("1", "B", "Band", "Rec", track: 2),
            Tr("2", "A", "Band", "Rec", track: 1),
            Tr("3", "Other", "Else", "Rec2"),
        ]);

        var byBand = catalog.TracksByArtist("Band");

        Assert.Equal(["A", "B"], byBand.Select(track => track.DisplayTitle).ToArray());
    }

    [Fact]
    public void TracksInGenre_FiltersByGenre()
    {
        var catalog = new MusicCatalog(
        [
            Tr("1", "T1", "X", "A", genre: "Jazz"),
            Tr("2", "T2", "Y", "B", genre: "Rock"),
        ]);

        Assert.Equal("T1", Assert.Single(catalog.TracksInGenre("Jazz")).DisplayTitle);
    }

    [Fact]
    public void Genres_AreDistinctAndSorted()
    {
        var catalog = new MusicCatalog(
        [
            Tr("1", "T1", "X", "A", genre: "Rock"),
            Tr("2", "T2", "Y", "B", genre: "Jazz"),
            Tr("3", "T3", "Z", "C", genre: "Rock"),
        ]);

        Assert.Equal(["Jazz", "Rock"], catalog.Genres());
    }
}
