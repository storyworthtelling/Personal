using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusPhone.Tests.Storage;

public class MusicCatalogCacheTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly MusicCatalogCache cache;

    public MusicCatalogCacheTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        cache = new MusicCatalogCache(connection);
    }

    public void Dispose() => connection.Dispose();

    private static Track Tr(string id, string title = "Song", string? artRef = null) =>
        new(id, title, "Artist", "Album", "Artist", 1, 1, "Rock", TimeSpan.FromMinutes(3), artRef);

    [Fact]
    public void Upsert_ThenLoadAll_RoundTrips()
    {
        cache.Upsert([Tr("1", "First"), Tr("2", "Second", artRef: "/art/2.jpg")]);

        var tracks = cache.LoadAll().OrderBy(track => track.Id).ToList();

        Assert.Equal(2, tracks.Count);
        Assert.Equal("First", tracks[0].Title);
        Assert.Null(tracks[0].ArtRef);
        Assert.Equal("/art/2.jpg", tracks[1].ArtRef);
        Assert.Equal(TimeSpan.FromMinutes(3), tracks[1].Duration);
    }

    [Fact]
    public void Upsert_UpdatesExistingTrackById()
    {
        cache.Upsert([Tr("1", "Old")]);
        cache.Upsert([Tr("1", "New")]);

        var track = Assert.Single(cache.LoadAll());
        Assert.Equal("New", track.Title);
    }

    [Fact]
    public void Delete_RemovesTrack()
    {
        cache.Upsert([Tr("1"), Tr("2")]);

        cache.Delete("1");

        Assert.Equal("2", Assert.Single(cache.LoadAll()).Id);
    }

    [Fact]
    public void Clear_EmptiesTheCache()
    {
        cache.Upsert([Tr("1"), Tr("2")]);

        cache.Clear();

        Assert.Empty(cache.LoadAll());
    }

    [Fact]
    public void LoadedTracks_BuildIntoACatalog()
    {
        cache.Upsert([Tr("1", "Alpha"), Tr("2", "Bravo")]);

        var catalog = new MusicCatalog(cache.LoadAll());

        Assert.Equal(["Alpha", "Bravo"], catalog.Songs().Select(track => track.DisplayTitle).ToArray());
    }
}
