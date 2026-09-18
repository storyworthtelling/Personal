using Codex.Domain.Timeline;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

/// <summary>
/// Records items the owner deleted but that the OS won't let us remove at the
/// source — today only texts, which can be trashed remotely only if the bridge
/// supports it (otherwise they are hidden here permanently). Kept keyed by
/// (kind, source id) like the cache so a hidden item is filtered out of every
/// view.
/// </summary>
public class HiddenItemStore
{
    private readonly SqliteConnection connection;

    public HiddenItemStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS hiddenItems (
                kind INTEGER NOT NULL,
                sourceId TEXT NOT NULL,
                PRIMARY KEY (kind, sourceId)
            )
            """;
        create.ExecuteNonQuery();
    }

    public void Hide(TimelineItemKind kind, string sourceId)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT OR IGNORE INTO hiddenItems (kind, sourceId) VALUES ($kind, $sourceId)";
        insert.Parameters.AddWithValue("$kind", (int)kind);
        insert.Parameters.AddWithValue("$sourceId", sourceId);
        insert.ExecuteNonQuery();
    }

    /// <summary>Withdraws a hide — the un-file counterpart to <see cref="Hide"/>, so a
    /// restored item is not filtered right back out of the merge on the next reload.</summary>
    public void Unhide(TimelineItemKind kind, string sourceId)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText =
            "DELETE FROM hiddenItems WHERE kind = $kind AND sourceId = $sourceId";
        delete.Parameters.AddWithValue("$kind", (int)kind);
        delete.Parameters.AddWithValue("$sourceId", sourceId);
        delete.ExecuteNonQuery();
    }

    public IReadOnlySet<(TimelineItemKind Kind, string SourceId)> LoadHidden()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT kind, sourceId FROM hiddenItems";
        using var reader = select.ExecuteReader();

        var hidden = new HashSet<(TimelineItemKind, string)>();
        while (reader.Read())
        {
            hidden.Add(((TimelineItemKind)reader.GetInt32(0), reader.GetString(1)));
        }
        return hidden;
    }

    /// <summary>Removes hidden items from a view (paired with the merge/load pipeline).</summary>
    public IEnumerable<TimelineItem> Filter(IEnumerable<TimelineItem> items)
    {
        var hidden = LoadHidden();
        return items.Where(item => !hidden.Contains((item.Kind, item.SourceId)));
    }
}
