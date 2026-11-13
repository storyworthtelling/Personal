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
            CREATE TABLE IF NOT EXISTS unfiled (
                sourceId TEXT NOT NULL PRIMARY KEY,
                unfiledAtUtc TEXT NOT NULL
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

        // Filed again: no longer unfiled.
        using var forget = connection.CreateCommand();
        forget.CommandText = "DELETE FROM unfiled WHERE sourceId = $id";
        forget.Parameters.AddWithValue("$id", ghost.Item.SourceId);
        forget.ExecuteNonQuery();
    }

    /// <summary>
    /// Takes a ghost out of Filed and remembers when, so the backup — a union — does not bring it back.
    /// </summary>
    public void RemoveGhost(string sourceId, DateTimeOffset unfiledAtUtc)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ghosts WHERE sourceId = $id";
        delete.Parameters.AddWithValue("$id", sourceId);
        delete.ExecuteNonQuery();

        RememberUnfiled([new UnfiledGhost(sourceId, unfiledAtUtc)]);
    }

    /// <summary>Keeps unfiled entries (this phone's, or the backup's) so a merge honours them after a restart.</summary>
    public void RememberUnfiled(IEnumerable<UnfiledGhost> entries)
    {
        foreach (var entry in entries)
        {
            using var upsert = connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO unfiled (sourceId, unfiledAtUtc) VALUES ($id, $at)
                ON CONFLICT(sourceId) DO UPDATE SET unfiledAtUtc = excluded.unfiledAtUtc
                WHERE excluded.unfiledAtUtc > unfiled.unfiledAtUtc
                """;
            upsert.Parameters.AddWithValue("$id", entry.SourceId);
            upsert.Parameters.AddWithValue("$at", entry.UnfiledAtUtc.ToUniversalTime().ToString("O"));
            upsert.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<UnfiledGhost> LoadUnfiled()
    {
        var entries = new List<UnfiledGhost>();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT sourceId, unfiledAtUtc FROM unfiled";
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new UnfiledGhost(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture)));
        }
        return entries;
    }

    /// <summary>How many ghosts there are and how many characters they take, read by SQLite without loading any of
    /// them (efficiency audit §1: the field notes used to deserialize the whole archive once a sync to add this up).</summary>
    public ArchiveSize MeasureArchive()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT COUNT(*), COALESCE(SUM(LENGTH(item)), 0) FROM ghosts";
        using var reader = select.ExecuteReader();
        reader.Read();
        return new ArchiveSize(reader.GetInt32(0), reader.GetInt64(1));
    }

    /// <summary>
    /// A short string that changes whenever a ghost is filed or unfiled: counts, latest times and total length of
    /// both tables. The backup sync compares it with the one it last confirmed, so an unchanged archive is not
    /// downloaded again. Read without loading a ghost.
    /// </summary>
    public string Fingerprint()
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT (SELECT COUNT(*) FROM ghosts), (SELECT COALESCE(MAX(filedAtUtc), '') FROM ghosts),
                   (SELECT COALESCE(SUM(LENGTH(sourceId)), 0) FROM ghosts),
                   (SELECT COUNT(*) FROM unfiled), (SELECT COALESCE(MAX(unfiledAtUtc), '') FROM unfiled),
                   (SELECT COALESCE(SUM(LENGTH(sourceId)), 0) FROM unfiled)
            """;
        using var reader = select.ExecuteReader();
        reader.Read();
        return string.Join('/', Enumerable.Range(0, 6).Select(column => reader.GetValue(column).ToString()));
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

    /// <summary>How much of a body stands in for a message with no snippet, in a search entry.</summary>
    public const int ExcerptChars = 300;

    /// <summary>
    /// A short summary of every ghost filed at or before <paramref name="settledBy"/>, for the
    /// Filed search. SQLite pulls the few fields out of each stored item itself, so the bodies
    /// are never deserialized or held in memory — that was the cost of listing everything.
    /// </summary>
    public IReadOnlyList<FiledEntry> LoadEntries(DateTimeOffset settledBy)
    {
        var entries = new List<FiledEntry>();
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT sourceId, filedAtUtc,
                   json_extract(item, '$.TimestampUtc'),
                   json_extract(item, '$.Kind'),
                   json_extract(item, '$.Incoming'),
                   json_extract(item, '$.ContactKey'),
                   substr(json_extract(item, '$.Subject'), 1, 200),
                   substr(coalesce(json_extract(item, '$.Snippet'), json_extract(item, '$.Body')), 1, $excerpt)
            FROM ghosts
            WHERE filedAtUtc <= $settled
            """;
        select.Parameters.AddWithValue("$excerpt", ExcerptChars);
        select.Parameters.AddWithValue("$settled", settledBy.ToUniversalTime().ToString("O"));
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new FiledEntry(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                (TimelineItemKind)reader.GetInt32(3),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return entries;
    }

    /// <summary>The whole ghosts for these ids, in the order asked; an id no longer filed is skipped.</summary>
    public IReadOnlyList<Ghost> LoadGhosts(IReadOnlyList<string> sourceIds)
    {
        var found = new Dictionary<string, Ghost>();
        foreach (var id in sourceIds.Distinct())
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT filedAtUtc, item FROM ghosts WHERE sourceId = $id";
            select.Parameters.AddWithValue("$id", id);
            using var reader = select.ExecuteReader();
            if (reader.Read() && JsonSerializer.Deserialize<TimelineItem>(reader.GetString(1)) is { } item)
            {
                found[id] = new Ghost(item, DateTimeOffset.Parse(reader.GetString(0)));
            }
        }
        return sourceIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
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

/// <summary>The ghost archive's size: how many, and how many characters of stored JSON.</summary>
public readonly record struct ArchiveSize(int Count, long Bytes);
