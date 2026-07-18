using System.Text.Json;
using FocusPhone.Domain.Timeline;
using Microsoft.Data.Sqlite;

namespace FocusPhone.Infrastructure.Storage;

/// <summary>
/// Local SQLite cache of timeline items from all sources (call log, messages,
/// email) so the Communication app renders instantly and offline. Items are
/// keyed by (kind, source id), matching TimelineMerger's deduplication. The
/// email-specific and unified-inbox columns were added after the original
/// six-column schema, so existing databases are migrated in place by adding any
/// missing columns.
/// </summary>
public class TimelineCache
{
    // Columns beyond the original six, added by migration. Order is not load-bearing.
    private static readonly (string Name, string Type)[] AddedColumns =
    {
        ("subject", "TEXT"),
        ("body", "TEXT"),
        ("fromAddress", "TEXT"),
        ("recipients", "TEXT"),
        ("carbonCopy", "TEXT"),
        ("messageId", "TEXT"),
        ("inReplyTo", "TEXT"),
        ("groupId", "TEXT"),
        ("participants", "TEXT"),
        ("externalConversationId", "TEXT"),
        ("unread", "INTEGER NOT NULL DEFAULT 0"),
        ("attachments", "TEXT"),
    };

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
        EnsureColumns();
    }

    private void EnsureColumns()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(timelineItems)";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1)); // column 1 = name
            }
        }
        foreach (var (name, type) in AddedColumns)
        {
            if (!existing.Contains(name))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE timelineItems ADD COLUMN {name} {type}";
                alter.ExecuteNonQuery();
            }
        }
    }

    public void Upsert(IEnumerable<TimelineItem> items)
    {
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO timelineItems (
                kind, sourceId, contactKey, timestampUtc, snippet, incoming,
                subject, body, fromAddress, recipients, carbonCopy, messageId, inReplyTo,
                groupId, participants, externalConversationId, unread, attachments)
            VALUES (
                $kind, $sourceId, $contactKey, $timestampUtc, $snippet, $incoming,
                $subject, $body, $fromAddress, $recipients, $carbonCopy, $messageId, $inReplyTo,
                $groupId, $participants, $externalConversationId, $unread, $attachments)
            ON CONFLICT (kind, sourceId) DO UPDATE SET
                contactKey = excluded.contactKey,
                timestampUtc = excluded.timestampUtc,
                snippet = excluded.snippet,
                incoming = excluded.incoming,
                subject = excluded.subject,
                body = excluded.body,
                fromAddress = excluded.fromAddress,
                recipients = excluded.recipients,
                carbonCopy = excluded.carbonCopy,
                messageId = excluded.messageId,
                inReplyTo = excluded.inReplyTo,
                groupId = excluded.groupId,
                participants = excluded.participants,
                externalConversationId = excluded.externalConversationId,
                unread = excluded.unread,
                attachments = excluded.attachments
            """;
        var parameters = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[]
        {
            "$kind", "$sourceId", "$contactKey", "$timestampUtc", "$snippet", "$incoming",
            "$subject", "$body", "$fromAddress", "$recipients", "$carbonCopy", "$messageId", "$inReplyTo",
            "$groupId", "$participants", "$externalConversationId", "$unread", "$attachments",
        })
        {
            var parameter = upsert.CreateParameter();
            parameter.ParameterName = name;
            upsert.Parameters.Add(parameter);
            parameters[name] = parameter;
        }

        foreach (var item in items)
        {
            parameters["$kind"].Value = (int)item.Kind;
            parameters["$sourceId"].Value = item.SourceId;
            parameters["$contactKey"].Value = item.ContactKey;
            parameters["$timestampUtc"].Value = item.TimestampUtc.ToString("O");
            parameters["$snippet"].Value = Nullable(item.Snippet);
            parameters["$incoming"].Value = item.Incoming ? 1 : 0;
            parameters["$subject"].Value = Nullable(item.Subject);
            parameters["$body"].Value = Nullable(item.Body);
            parameters["$fromAddress"].Value = Nullable(item.FromAddress);
            parameters["$recipients"].Value = Nullable(EncodeList(item.Recipients));
            parameters["$carbonCopy"].Value = Nullable(EncodeList(item.CarbonCopy));
            parameters["$messageId"].Value = Nullable(item.MessageId);
            parameters["$inReplyTo"].Value = Nullable(item.InReplyTo);
            parameters["$groupId"].Value = Nullable(item.GroupId);
            parameters["$participants"].Value = Nullable(EncodeList(item.Participants));
            parameters["$externalConversationId"].Value = Nullable(item.ExternalConversationId);
            parameters["$unread"].Value = item.Unread ? 1 : 0;
            parameters["$attachments"].Value = Nullable(EncodeAttachments(item.Attachments));
            upsert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Delete(TimelineItemKind kind, string sourceId)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM timelineItems WHERE kind = $kind AND sourceId = $sourceId";
        delete.Parameters.AddWithValue("$kind", (int)kind);
        delete.Parameters.AddWithValue("$sourceId", sourceId);
        delete.ExecuteNonQuery();
    }

    public IReadOnlyList<TimelineItem> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT kind, sourceId, contactKey, timestampUtc, snippet, incoming,
                   subject, body, fromAddress, recipients, carbonCopy, messageId, inReplyTo,
                   groupId, participants, externalConversationId, unread, attachments
            FROM timelineItems
            """;
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
                reader.GetString(1))
            {
                Subject = ReadString(reader, 6),
                Body = ReadString(reader, 7),
                FromAddress = ReadString(reader, 8),
                Recipients = DecodeList(ReadString(reader, 9)),
                CarbonCopy = DecodeList(ReadString(reader, 10)),
                MessageId = ReadString(reader, 11),
                InReplyTo = ReadString(reader, 12),
                GroupId = ReadString(reader, 13),
                Participants = DecodeList(ReadString(reader, 14)),
                ExternalConversationId = ReadString(reader, 15),
                Unread = !reader.IsDBNull(16) && reader.GetInt32(16) != 0,
                Attachments = DecodeAttachments(ReadString(reader, 17)),
            });
        }
        return items;
    }

    private static object Nullable(string? value) => (object?)value ?? DBNull.Value;

    private static string? ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // Addresses and keys never contain newlines, so a newline join is a safe,
    // dependency-free list encoding.
    private static string? EncodeList(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join('\n', values);

    private static IReadOnlyList<string> DecodeList(string? encoded) =>
        string.IsNullOrEmpty(encoded) ? [] : encoded.Split('\n');

    private static string? EncodeAttachments(IReadOnlyList<AttachmentRef> attachments) =>
        attachments.Count == 0 ? null : JsonSerializer.Serialize(attachments);

    private static IReadOnlyList<AttachmentRef> DecodeAttachments(string? encoded) =>
        string.IsNullOrEmpty(encoded)
            ? []
            : JsonSerializer.Deserialize<List<AttachmentRef>>(encoded) ?? [];
}
