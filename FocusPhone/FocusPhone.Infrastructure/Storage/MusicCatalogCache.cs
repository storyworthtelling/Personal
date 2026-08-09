using FocusPhone.Domain.Music;
using Microsoft.Data.Sqlite;

namespace FocusPhone.Infrastructure.Storage;

/// <summary>
/// Local SQLite cache of the music catalog (one row per track) so the iPod-style
/// browse renders instantly and offline instead of re-scanning Drive every time.
/// Follows the repo's raw-SQLite convention (see <see cref="TimelineCache"/>): a
/// connection is supplied, the table is created on demand, and upserts key on the
/// Drive file id.
/// </summary>
public class MusicCatalogCache
{
    private readonly SqliteConnection connection;

    public MusicCatalogCache(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS tracks (
                id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                album TEXT NOT NULL,
                albumArtist TEXT NOT NULL,
                trackNumber INTEGER NOT NULL,
                discNumber INTEGER NOT NULL,
                genre TEXT NOT NULL,
                durationTicks INTEGER NOT NULL,
                artRef TEXT,
                modifiedTimeTicks INTEGER NOT NULL DEFAULT 0,
                sizeBytes INTEGER NOT NULL DEFAULT 0
            )
            """;
        create.ExecuteNonQuery();

        // Older caches predate the change-signature columns the incremental scan needs; add them in place so
        // an upgrade keeps its rows (they read as version 0, i.e. "unknown", so the next scan re-reads once).
        EnsureColumn("modifiedTimeTicks");
        EnsureColumn("sizeBytes");
    }

    private void EnsureColumn(string name)
    {
        var present = false;
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(tracks)";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }
        }

        if (present)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE tracks ADD COLUMN {name} INTEGER NOT NULL DEFAULT 0";
        alter.ExecuteNonQuery();
    }

    /// <summary>Upserts tracks without change signatures (version 0) — used by tests and callers that don't scan.</summary>
    public void Upsert(IEnumerable<Track> tracks) =>
        Upsert(tracks.Select(track => (track, default(MusicFileVersion))));

    /// <summary>
    /// Upserts each track together with its file's change signature, so a later scan can tell what is
    /// unchanged. One transaction per call, so the scanner can batch a chunk of parallel reads into a
    /// single serialized write.
    /// </summary>
    public void Upsert(IEnumerable<(Track Track, MusicFileVersion Version)> entries)
    {
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO tracks (
                id, title, artist, album, albumArtist, trackNumber, discNumber, genre, durationTicks, artRef,
                modifiedTimeTicks, sizeBytes)
            VALUES (
                $id, $title, $artist, $album, $albumArtist, $trackNumber, $discNumber, $genre, $durationTicks, $artRef,
                $modifiedTimeTicks, $sizeBytes)
            ON CONFLICT (id) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                albumArtist = excluded.albumArtist,
                trackNumber = excluded.trackNumber,
                discNumber = excluded.discNumber,
                genre = excluded.genre,
                durationTicks = excluded.durationTicks,
                artRef = excluded.artRef,
                modifiedTimeTicks = excluded.modifiedTimeTicks,
                sizeBytes = excluded.sizeBytes
            """;

        var parameters = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[]
        {
            "$id", "$title", "$artist", "$album", "$albumArtist",
            "$trackNumber", "$discNumber", "$genre", "$durationTicks", "$artRef",
            "$modifiedTimeTicks", "$sizeBytes",
        })
        {
            var parameter = upsert.CreateParameter();
            parameter.ParameterName = name;
            upsert.Parameters.Add(parameter);
            parameters[name] = parameter;
        }

        foreach (var (track, version) in entries)
        {
            parameters["$id"].Value = track.Id;
            parameters["$title"].Value = track.Title;
            parameters["$artist"].Value = track.ArtistName;
            parameters["$album"].Value = track.AlbumName;
            parameters["$albumArtist"].Value = track.AlbumArtist;
            parameters["$trackNumber"].Value = track.TrackNumber;
            parameters["$discNumber"].Value = track.DiscNumber;
            parameters["$genre"].Value = track.Genre;
            parameters["$durationTicks"].Value = track.Duration.Ticks;
            parameters["$artRef"].Value = (object?)track.ArtRef ?? DBNull.Value;
            parameters["$modifiedTimeTicks"].Value = version.ModifiedTimeTicks;
            parameters["$sizeBytes"].Value = version.SizeBytes;
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The change signature of every cached track, keyed by Drive file id — the scan's "what I already have".</summary>
    public IReadOnlyDictionary<string, MusicFileVersion> LoadVersions()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id, modifiedTimeTicks, sizeBytes FROM tracks";
        using var reader = select.ExecuteReader();

        var versions = new Dictionary<string, MusicFileVersion>();
        while (reader.Read())
        {
            versions[reader.GetString(0)] = new MusicFileVersion(reader.GetInt64(1), reader.GetInt64(2));
        }

        return versions;
    }

    public void Delete(string id)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM tracks WHERE id = $id";
        delete.Parameters.AddWithValue("$id", id);
        delete.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var clear = connection.CreateCommand();
        clear.CommandText = "DELETE FROM tracks";
        clear.ExecuteNonQuery();
    }

    public IReadOnlyList<Track> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, title, artist, album, albumArtist, trackNumber, discNumber, genre, durationTicks, artRef
            FROM tracks
            """;
        using var reader = select.ExecuteReader();

        var tracks = new List<Track>();
        while (reader.Read())
        {
            tracks.Add(new Track(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetString(7),
                TimeSpan.FromTicks(reader.GetInt64(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return tracks;
    }
}
