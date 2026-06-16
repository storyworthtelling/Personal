using FocusPhone.Domain.Timeline;
using Microsoft.Data.Sqlite;

namespace FocusPhone.Infrastructure.Storage;

/// <summary>
/// Local SQLite cache of timeline items from all sources (call log, messages,
/// email) so the Communication app renders instantly and offline. Items are
/// keyed by (kind, source id), matching TimelineMerger's deduplication.
/// </summary>
public class TimelineCache
{
    private readonly SqliteConnection connection;

    public TimelineCache(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS timelineItems (
                kind INTEGER NOT NULL,
                sourceId TEXT NOT NULL,
                contactKey TEXT NOT NULL,
                timestampUtc TEXT NOT NULL,
                snippet TEXT,
                incoming INTEGER NOT NULL,
                PRIMARY KEY (kind, sourceId)
            )
            """;
        create.ExecuteNonQuery();
    }

    public void Upsert(IEnumerable<TimelineItem> items)
    {
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO timelineItems (kind, sourceId, contactKey, timestampUtc, snippet, incoming)
            VALUES ($kind, $sourceId, $contactKey, $timestampUtc, $snippet, $incoming)
            ON CONFLICT (kind, sourceId) DO UPDATE SET
                contactKey = excluded.contactKey,
                timestampUtc = excluded.timestampUtc,
                snippet = excluded.snippet,
                incoming = excluded.incoming
            """;
        var kind = upsert.CreateParameter(); kind.ParameterName = "$kind"; upsert.Parameters.Add(kind);
        var sourceId = upsert.CreateParameter(); sourceId.ParameterName = "$sourceId"; upsert.Parameters.Add(sourceId);
        var contactKey = upsert.CreateParameter(); contactKey.ParameterName = "$contactKey"; upsert.Parameters.Add(contactKey);
        var timestampUtc = upsert.CreateParameter(); timestampUtc.ParameterName = "$timestampUtc"; upsert.Parameters.Add(timestampUtc);
        var snippet = upsert.CreateParameter(); snippet.ParameterName = "$snippet"; upsert.Parameters.Add(snippet);
        var incoming = upsert.CreateParameter(); incoming.ParameterName = "$incoming"; upsert.Parameters.Add(incoming);

        foreach (var item in items)
        {
            kind.Value = (int)item.Kind;
            sourceId.Value = item.SourceId;
            contactKey.Value = item.ContactKey;
            timestampUtc.Value = item.TimestampUtc.ToString("O");
            snippet.Value = (object?)item.Snippet ?? DBNull.Value;
            incoming.Value = item.Incoming ? 1 : 0;
            upsert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<TimelineItem> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText =
            "SELECT kind, sourceId, contactKey, timestampUtc, snippet, incoming FROM timelineItems";
        using var reader = select.ExecuteReader();

        var items = new List<TimelineItem>();
        while (reader.Read())
        {
            items.Add(new TimelineItem(
                (TimelineItemKind)reader.GetInt32(0),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) != 0,
                reader.GetString(1)));
        }
        return items;
    }
}
