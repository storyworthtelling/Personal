using Codex.Domain.Music;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

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

    // A file that was split into two rows is still one physical file to the scan's
    // delete list — it only ever names the file's own id, never knowing it produced
    // two rows. Deleting by that id has to take both with it, or a removed file would
    // leave its hidden-track half behind forever.
    [Fact]
    public void Delete_RemovesEveryHiddenTrackSplitSegmentOfTheDeletedFile()
    {
        cache.Upsert([
            Tr("file#0") with { SourceFileId = "file", ClipStart = TimeSpan.Zero },
            Tr("file#1") with { SourceFileId = "file", ClipStart = TimeSpan.FromMinutes(4) },
            Tr("2"),
        ]);

        cache.Delete("file");

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

    [Fact]
    public void Upsert_ThenLoadAll_KeepsTheTempo()
    {
        cache.Upsert([Tr("1") with { Bpm = 128 }]);

        Assert.Equal(128, cache.LoadAll().Single().Bpm);
    }

    // A cache written before tempo existed must keep its rows and read them back as
    // unknown, so the next scan fills them in rather than the library emptying.
    [Fact]
    public void Cache_AddsTheTempoColumnToAnOlderCache()
    {
        using var older = new SqliteConnection("Data Source=:memory:");
        older.Open();
        using (var create = older.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE tracks (
                    id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
                    album TEXT NOT NULL, albumArtist TEXT NOT NULL, trackNumber INTEGER NOT NULL,
                    discNumber INTEGER NOT NULL, genre TEXT NOT NULL, durationTicks INTEGER NOT NULL,
                    artRef TEXT);
                INSERT INTO tracks VALUES ('1','Old','A','Al','A',1,1,'Rock',0,NULL);
                """;
            create.ExecuteNonQuery();
        }

        var upgraded = new MusicCatalogCache(older).LoadAll().Single();

        Assert.Equal("Old", upgraded.Title);
        Assert.Equal(0, upgraded.Bpm);
    }

    // What the cache remembers about the reader that filled it, so a reader that has
    // learned to read a new tag can tell that every row predates it.
    [Fact]
    public void ReaderVersion_StartsAtZero()
    {
        Assert.Equal(0, cache.ReaderVersion);
    }

    [Fact]
    public void ReaderVersion_RoundTrips()
    {
        cache.ReaderVersion = 2;

        Assert.Equal(2, cache.ReaderVersion);
        Assert.Equal(2, new MusicCatalogCache(connection).ReaderVersion);
    }

    [Fact]
    public void Upsert_ThenLoadAll_RoundTripsAHiddenTrackSplitSegment()
    {
        cache.Upsert([Tr("file#1", "Bonus") with { SourceFileId = "file", ClipStart = TimeSpan.FromMinutes(4) }]);

        var track = cache.LoadAll().Single();

        Assert.Equal("file", track.SourceFileId);
        Assert.Equal(TimeSpan.FromMinutes(4), track.ClipStart);
        Assert.Equal("file", track.ResolvedSourceId);
    }

    // An ordinary track — never split — must not come back looking like a clip that
    // merely starts at zero; ClipStart has to round-trip as genuinely absent.
    [Fact]
    public void Upsert_ThenLoadAll_AnOrdinaryTrackHasNoClipStart()
    {
        cache.Upsert([Tr("1")]);

        var track = cache.LoadAll().Single();

        Assert.Null(track.SourceFileId);
        Assert.Null(track.ClipStart);
        Assert.Equal("1", track.ResolvedSourceId);
    }

    [Fact]
    public void Cache_AddsTheSplitColumnsToAnOlderCache()
    {
        using var older = new SqliteConnection("Data Source=:memory:");
        older.Open();
        using (var create = older.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE tracks (
                    id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
                    album TEXT NOT NULL, albumArtist TEXT NOT NULL, trackNumber INTEGER NOT NULL,
                    discNumber INTEGER NOT NULL, genre TEXT NOT NULL, durationTicks INTEGER NOT NULL,
                    artRef TEXT, bpm INTEGER NOT NULL DEFAULT 0);
                INSERT INTO tracks VALUES ('1','Old','A','Al','A',1,1,'Rock',0,NULL,0);
                """;
            create.ExecuteNonQuery();
        }

        var upgraded = new MusicCatalogCache(older).LoadAll().Single();

        Assert.Equal("Old", upgraded.Title);
        Assert.Null(upgraded.SourceFileId);
        Assert.Null(upgraded.ClipStart);
    }
}
