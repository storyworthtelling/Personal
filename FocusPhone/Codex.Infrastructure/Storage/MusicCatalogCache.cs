using Codex.Domain.Music;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

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
                sizeBytes INTEGER NOT NULL DEFAULT 0,
                bpm INTEGER NOT NULL DEFAULT 0,
                sourceId TEXT,
                clipStartTicks INTEGER NOT NULL DEFAULT 0,
                bitrate INTEGER NOT NULL DEFAULT 0,
                replayGain REAL,
                readerVersion INTEGER NOT NULL DEFAULT 0
            )
            """;
        create.ExecuteNonQuery();

        using (var meta = connection.CreateCommand())
        {
            meta.CommandText = "CREATE TABLE IF NOT EXISTS meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL)";
            meta.ExecuteNonQuery();
        }

        // Duplicates set aside by the scan: files still in Drive, so the scan must keep knowing
        // them (or it reads them as new every time), but not songs in the catalog. Keyed by file
        // id, with the copy each lost to, so losing that copy brings the duplicate back.
        using (var setAside = connection.CreateCommand())
        {
            setAside.CommandText = """
                CREATE TABLE IF NOT EXISTS setAside (
                    id TEXT NOT NULL PRIMARY KEY,
                    winnerId TEXT NOT NULL,
                    modifiedTimeTicks INTEGER NOT NULL,
                    sizeBytes INTEGER NOT NULL,
                    readerVersion INTEGER NOT NULL
                )
                """;
            setAside.ExecuteNonQuery();
        }

        // Older caches predate the change-signature columns the incremental scan needs; add them in place so
        // an upgrade keeps its rows (they read as version 0, i.e. "unknown", so the next scan re-reads once).
        EnsureColumn("modifiedTimeTicks");
        EnsureColumn("sizeBytes");

        // Tempo arrived with the workout. It defaults to 0 = unknown, so an older
        // cache keeps every row and the next scan fills the column in from the tags.
        EnsureColumn("bpm");

        // Hidden-track splitting arrived after tempo. sourceId is nullable and NULL by
        // default — "not a split segment" — so an older row reads back with no
        // SourceFileId/ClipStart at all rather than looking like a clip starting at 0.
        EnsureColumn("sourceId", "TEXT");
        EnsureColumn("clipStartTicks");

        // Bitrate arrived with duplicate detection. Zero = unmeasured, so an older
        // cache keeps every row and the next scan fills it in from the tags.
        EnsureColumn("bitrate");

        // ReplayGain arrived with sound check (W7.3). NULL = no tag, so an older cache reads as "no gain" until
        // the reader's version bump re-reads every file once.
        EnsureColumn("replayGain", "REAL");

        // Which reader filled each row, so a re-read after the reader learns a new tag is kept
        // row by row (see StaleFiles). An older cache's rows were filled by whichever reader last
        // finished a whole pass, which is what the meta row recorded, so they take that.
        if (EnsureColumn("readerVersion"))
        {
            using var seed = connection.CreateCommand();
            seed.CommandText = "UPDATE tracks SET readerVersion = $version";
            seed.Parameters.AddWithValue("$version", ReaderVersion);
            seed.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Which generation of the tag reader filled this cache.
    ///
    /// It exists because a reader that starts reading a new tag cannot tell, from the
    /// files alone, that its rows are stale: nothing about the files changed. Bumping
    /// the reader's version is how a scan is told to go round once more.
    /// </summary>
    public int ReaderVersion
    {
        get
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT value FROM meta WHERE key = 'readerVersion'";
            return select.ExecuteScalar() is string stored && int.TryParse(stored, out var version) ? version : 0;
        }
        set
        {
            using var upsert = connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO meta (key, value) VALUES ('readerVersion', $value)
                ON CONFLICT (key) DO UPDATE SET value = excluded.value
                """;
            upsert.Parameters.AddWithValue("$value", value.ToString());
            upsert.ExecuteNonQuery();
        }
    }

    // True when the column was missing and has just been added.
    private bool EnsureColumn(string name, string sqlType = "INTEGER NOT NULL DEFAULT 0")
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
            return false;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE tracks ADD COLUMN {name} {sqlType}";
        alter.ExecuteNonQuery();
        return true;
    }

    /// <summary>Upserts tracks without change signatures (version 0) — used by tests and callers that don't scan.</summary>
    public void Upsert(IEnumerable<Track> tracks) =>
        Upsert(tracks.Select(track => (track, default(MusicFileVersion))));

    /// <summary>
    /// Upserts each track together with its file's change signature, so a later scan can tell what is
    /// unchanged. One transaction per call, so the scanner can batch a chunk of parallel reads into a
    /// single serialized write. <paramref name="readerVersion"/> is the tag reader that read them.
    /// </summary>
    public void Upsert(IEnumerable<(Track Track, MusicFileVersion Version)> entries, int readerVersion = 0)
    {
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO tracks (
                id, title, artist, album, albumArtist, trackNumber, discNumber, genre, durationTicks, artRef,
                modifiedTimeTicks, sizeBytes, bpm, sourceId, clipStartTicks, bitrate, replayGain, readerVersion)
            VALUES (
                $id, $title, $artist, $album, $albumArtist, $trackNumber, $discNumber, $genre, $durationTicks, $artRef,
                $modifiedTimeTicks, $sizeBytes, $bpm, $sourceId, $clipStartTicks, $bitrate, $replayGain, $readerVersion)
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
                sizeBytes = excluded.sizeBytes,
                bpm = excluded.bpm,
                sourceId = excluded.sourceId,
                clipStartTicks = excluded.clipStartTicks,
                bitrate = excluded.bitrate,
                replayGain = excluded.replayGain,
                readerVersion = excluded.readerVersion
            """;

        var parameters = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[]
        {
            "$id", "$title", "$artist", "$album", "$albumArtist",
            "$trackNumber", "$discNumber", "$genre", "$durationTicks", "$artRef",
            "$modifiedTimeTicks", "$sizeBytes", "$bpm", "$sourceId", "$clipStartTicks", "$bitrate", "$replayGain",
            "$readerVersion",
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
            parameters["$bpm"].Value = track.Bpm;
            parameters["$sourceId"].Value = (object?)track.SourceFileId ?? DBNull.Value;
            parameters["$clipStartTicks"].Value = track.ClipStart?.Ticks ?? 0L;
            parameters["$bitrate"].Value = track.Bitrate;
            parameters["$replayGain"].Value = (object?)track.ReplayGainDb ?? DBNull.Value;
            parameters["$readerVersion"].Value = readerVersion;
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// The change signature of every cached track, keyed by Drive file id — the scan's "what I
    /// already have". A hidden-track split's rows carry their own generated <c>id</c>, never the
    /// source file's — <c>COALESCE(sourceId, id)</c> is what lets a split file's rows answer to
    /// the file id the scan actually diffs against (see <see cref="Delete"/>'s same handling).
    /// Without it, a split file matched nothing here on every single scan and was re-read every
    /// time regardless of whether it had changed.
    /// </summary>
    public IReadOnlyDictionary<string, MusicFileVersion> LoadVersions()
    {
        using var select = connection.CreateCommand();
        // Set-aside duplicates are files the scan has read too: without them here, each one was
        // "new" again on every scan, read, and set aside again, so the scan never settled.
        select.CommandText = """
            SELECT id, modifiedTimeTicks, sizeBytes FROM setAside
            UNION ALL
            SELECT COALESCE(sourceId, id), modifiedTimeTicks, sizeBytes FROM tracks
            """;
        using var reader = select.ExecuteReader();

        // Every split row from the same source file was written with that file's own version
        // (MusicScanner.ReadFileAsync computes one MusicFileVersion per file, before splitting),
        // so whichever row is seen last for a given file id agrees with the rest.
        var versions = new Dictionary<string, MusicFileVersion>();
        while (reader.Read())
        {
            versions[reader.GetString(0)] = new MusicFileVersion(reader.GetInt64(1), reader.GetInt64(2));
        }

        return versions;
    }

    /// <summary>
    /// The files (by Drive file id) last read by a tag reader older than
    /// <paramref name="readerVersion"/> — what a re-read still has to cover. Kept per row so a
    /// scan cut short by its time budget keeps every file it re-read; the next run picks up
    /// only what is left rather than the whole library again.
    /// </summary>
    public IReadOnlySet<string> StaleFiles(int readerVersion)
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT COALESCE(sourceId, id) FROM tracks WHERE readerVersion < $version
            UNION
            SELECT id FROM setAside WHERE readerVersion < $version
            """;
        select.Parameters.AddWithValue("$version", readerVersion);
        using var reader = select.ExecuteReader();
        var stale = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            stale.Add(reader.GetString(0));
        }

        return stale;
    }

    /// <summary>
    /// Takes duplicate copies out of the catalog while keeping their files known to the scan,
    /// each against the copy it lost to (see <see cref="DuplicateTracks.FindLosers"/>).
    /// </summary>
    public void SetAside(IEnumerable<(string LosingId, string WinnerId)> losers)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var (losingId, winnerId) in losers)
        {
            using var move = connection.CreateCommand();
            move.Transaction = transaction;
            move.CommandText = """
                INSERT OR REPLACE INTO setAside (id, winnerId, modifiedTimeTicks, sizeBytes, readerVersion)
                SELECT COALESCE(sourceId, id),
                       COALESCE((SELECT COALESCE(sourceId, id) FROM tracks WHERE id = $winner), $winner),
                       modifiedTimeTicks, sizeBytes, readerVersion
                FROM tracks WHERE id = $loser;
                DELETE FROM tracks WHERE id = $loser;
                """;
            move.Parameters.AddWithValue("$loser", losingId);
            move.Parameters.AddWithValue("$winner", winnerId);
            move.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Deletes by physical file id — which is all a scan ever names, since it diffs
    /// Drive listings, not cache rows. A hidden-track split leaves two rows under
    /// their own ids sharing that file as <c>sourceId</c>, so both have to go with it.
    /// A set-aside record of the file goes too. With <paramref name="releaseSetAside"/>
    /// (the file has left Drive) the duplicates set aside in its favour are forgotten as
    /// well, so the next scan reads one back in as the song; a scan that is only replacing
    /// the file's rows with a fresh read passes false and keeps them set aside.
    /// </summary>
    public void Delete(string id, bool releaseSetAside = true)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM tracks WHERE id = $id OR sourceId = $id;
            DELETE FROM setAside WHERE id = $id OR ($release AND winnerId = $id);
            """;
        delete.Parameters.AddWithValue("$id", id);
        delete.Parameters.AddWithValue("$release", releaseSetAside ? 1 : 0);
        delete.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var clear = connection.CreateCommand();
        clear.CommandText = "DELETE FROM tracks; DELETE FROM setAside;";
        clear.ExecuteNonQuery();
    }

    public IReadOnlyList<Track> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, title, artist, album, albumArtist, trackNumber, discNumber, genre, durationTicks, artRef, bpm,
                sourceId, clipStartTicks, bitrate, replayGain
            FROM tracks
            """;
        using var reader = select.ExecuteReader();

        var tracks = new List<Track>();
        while (reader.Read())
        {
            var sourceId = reader.IsDBNull(11) ? null : reader.GetString(11);
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
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt32(10),
                SourceFileId: sourceId,
                ClipStart: sourceId is null ? null : TimeSpan.FromTicks(reader.GetInt64(12)),
                Bitrate: reader.GetInt32(13),
                ReplayGainDb: reader.IsDBNull(14) ? null : reader.GetDouble(14)));
        }

        return tracks;
    }
}
