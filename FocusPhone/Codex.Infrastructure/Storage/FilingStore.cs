using System.Text.Json;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

/// <summary>
/// Where filing lives on disk: the ghost archive, and the tombstones that keep a filed
/// message from walking back in before the source has caught up.
///
/// Two tables of its own, never columns on the timeline cache. The cache mirrors the
/// source and is rebuilt from it whenever convenient; these are the opposite — once a
/// message is deleted at the source, the ghost row here is the only copy of it anywhere,
/// so nothing may treat this as derivable.
///
/// A ghost is stored as the whole serialized item rather than as columns. The archive is
/// read whole and filtered in memory (hundreds live, thousands filed), so there is nothing
/// to gain from a column per field — and a field the schema forgot to add would be a field
/// silently dropped from the last copy of a message.
/// </summary>
public class FilingStore
{
    private readonly SqliteConnection connection;

    public FilingStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS ghosts (
                sourceId TEXT NOT NULL PRIMARY KEY,
                filedAtUtc TEXT NOT NULL,
                item TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tombstones (
                sourceId TEXT NOT NULL PRIMARY KEY,
                filedAtUtc TEXT NOT NULL,
                failedAttempts INTEGER NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public void SaveGhost(Ghost ghost)
    {
        using var upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO ghosts (sourceId, filedAtUtc, item) VALUES ($id, $filed, $item)
            ON CONFLICT(sourceId) DO NOTHING
            """;
        upsert.Parameters.AddWithValue("$id", ghost.Item.SourceId);
        upsert.Parameters.AddWithValue("$filed", ghost.FiledAtUtc.ToUniversalTime().ToString("O"));
        upsert.Parameters.AddWithValue("$item", JsonSerializer.Serialize(ghost.Item));
        upsert.ExecuteNonQuery();
    }

    public void RemoveGhost(string sourceId)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ghosts WHERE sourceId = $id";
        delete.Parameters.AddWithValue("$id", sourceId);
        delete.ExecuteNonQuery();
    }

    public GhostArchive LoadArchive()
    {
        var ghosts = new List<Ghost>();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT filedAtUtc, item FROM ghosts";
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<TimelineItem>(reader.GetString(1));
            if (item is not null)
            {
                ghosts.Add(new Ghost(item, DateTimeOffset.Parse(reader.GetString(0))));
            }
        }
        return GhostArchive.From(ghosts);
    }

    /// <summary>
    /// Replaces the stored tombstones with what stands now. A replace rather than an
    /// append, because a confirmed delete DROPS its tombstone and the store has to forget
    /// it — an accumulating table would suppress that message forever.
    /// </summary>
    public void SaveTombstones(Tombstones tombstones)
    {
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM tombstones";
            clear.ExecuteNonQuery();
        }

        foreach (var tombstone in tombstones.All)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO tombstones (sourceId, filedAtUtc, failedAttempts)
                VALUES ($id, $filed, $attempts)
                """;
            insert.Parameters.AddWithValue("$id", tombstone.SourceId);
            insert.Parameters.AddWithValue("$filed", tombstone.FiledAtUtc.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$attempts", tombstone.FailedAttempts);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public Tombstones LoadTombstones()
    {
        var tombstones = new List<Tombstone>();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT sourceId, filedAtUtc, failedAttempts FROM tombstones";
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            tombstones.Add(new Tombstone(
                reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetInt32(2)));
        }
        return Tombstones.From(tombstones);
    }
}
