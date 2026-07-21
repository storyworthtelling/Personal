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
                artRef TEXT
            )
            """;
        create.ExecuteNonQuery();
    }

    public void Upsert(IEnumerable<Track> tracks)
    {
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO tracks (
                id, title, artist, album, albumArtist, trackNumber, discNumber, genre, durationTicks, artRef)
            VALUES (
                $id, $title, $artist, $album, $albumArtist, $trackNumber, $discNumber, $genre, $durationTicks, $artRef)
            ON CONFLICT (id) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                albumArtist = excluded.albumArtist,
                trackNumber = excluded.trackNumber,
                discNumber = excluded.discNumber,
                genre = excluded.genre,
                durationTicks = excluded.durationTicks,
                artRef = excluded.artRef
            """;

        var parameters = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[]
        {
            "$id", "$title", "$artist", "$album", "$albumArtist",
            "$trackNumber", "$discNumber", "$genre", "$durationTicks", "$artRef",
        })
        {
            var parameter = upsert.CreateParameter();
            parameter.ParameterName = name;
            upsert.Parameters.Add(parameter);
            parameters[name] = parameter;
        }

        foreach (var track in tracks)
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
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
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
