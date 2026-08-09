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

    [Fact]
    public void VersionedUpsert_RoundTripsThroughLoadVersions()
    {
        cache.Upsert([
            (Tr("1"), new MusicFileVersion(1000, 2048)),
            (Tr("2"), new MusicFileVersion(3000, 4096)),
        ]);

        var versions = cache.LoadVersions();

        Assert.Equal(new MusicFileVersion(1000, 2048), versions["1"]);
        Assert.Equal(new MusicFileVersion(3000, 4096), versions["2"]);
    }

    [Fact]
    public void VersionedUpsert_UpdatesSignatureOnReupsert()
    {
        cache.Upsert([(Tr("1"), new MusicFileVersion(1000, 2048))]);
        cache.Upsert([(Tr("1"), new MusicFileVersion(5000, 9000))]);

        Assert.Equal(new MusicFileVersion(5000, 9000), cache.LoadVersions()["1"]);
    }

    [Fact]
    public void TrackOnlyUpsert_StoresZeroVersion()
    {
        cache.Upsert([Tr("1")]);

        Assert.Equal(new MusicFileVersion(0, 0), cache.LoadVersions()["1"]);
    }

    [Fact]
    public void Migration_AddsVersionColumnsToAPreexistingTracksTable()
    {
        // Simulate an older cache: a tracks table without the change-signature columns.
        using (var legacy = connection.CreateCommand())
        {
            legacy.CommandText = "DROP TABLE tracks";
            legacy.ExecuteNonQuery();
        }

        using (var legacy = connection.CreateCommand())
        {
            legacy.CommandText = """
                CREATE TABLE tracks (
                    id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
                    album TEXT NOT NULL, albumArtist TEXT NOT NULL, trackNumber INTEGER NOT NULL,
                    discNumber INTEGER NOT NULL, genre TEXT NOT NULL, durationTicks INTEGER NOT NULL, artRef TEXT)
                """;
            legacy.ExecuteNonQuery();
        }

        // Re-opening the cache over the legacy table must add the columns without losing rows.
        var upgraded = new MusicCatalogCache(connection);
        upgraded.Upsert([(Tr("1"), new MusicFileVersion(7000, 1234))]);

        Assert.Equal(new MusicFileVersion(7000, 1234), upgraded.LoadVersions()["1"]);
    }
}
