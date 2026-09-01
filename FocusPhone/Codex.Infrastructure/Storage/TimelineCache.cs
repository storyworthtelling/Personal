using System.Text.Json;
using Codex.Domain.Timeline;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

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
        ("mailClass", "INTEGER"),
        ("htmlBody", "TEXT"),
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
                groupId, participants, externalConversationId, unread, attachments, mailClass, htmlBody)
            VALUES (
                $kind, $sourceId, $contactKey, $timestampUtc, $snippet, $incoming,
                $subject, $body, $fromAddress, $recipients, $carbonCopy, $messageId, $inReplyTo,
                $groupId, $participants, $externalConversationId, $unread, $attachments, $mailClass, $htmlBody)
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
                attachments = excluded.attachments,
                -- A classification is only ever ADDED to a row, never cleared by a later
                -- upsert: a re-sync re-reads the message from the source, which knows
                -- nothing about how Post sorted it, and would otherwise undo the judgement
                -- (and any correction the owner made) every half hour.
                mailClass = COALESCE(excluded.mailClass, timelineItems.mailClass),
                htmlBody = excluded.htmlBody
            """;
        var parameters = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[]
        {
            "$kind", "$sourceId", "$contactKey", "$timestampUtc", "$snippet", "$incoming",
            "$subject", "$body", "$fromAddress", "$recipients", "$carbonCopy", "$messageId", "$inReplyTo",
            "$groupId", "$participants", "$externalConversationId", "$unread", "$attachments",
            "$mailClass", "$htmlBody",
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
            parameters["$mailClass"].Value = item.MailClass is { } mailClass ? (int)mailClass : DBNull.Value;
            parameters["$htmlBody"].Value = Nullable(item.HtmlBody);
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

    // The columns every read selects, in the order Read expects them.
    private const string SelectColumns = """
        kind, sourceId, contactKey, timestampUtc, snippet, incoming,
        subject, body, fromAddress, recipients, carbonCopy, messageId, inReplyTo,
        groupId, participants, externalConversationId, unread, attachments, mailClass, htmlBody
        """;

    public IReadOnlyList<TimelineItem> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText = $"SELECT {SelectColumns} FROM timelineItems";
        return Read(select);
    }

    /// <summary>
    /// Loads a page of items most-recent first, so the timeline screen can render
    /// the newest conversations without materializing the whole table. Every
    /// cached writer stamps UTC timestamps, so ordering the ISO-8601 text
    /// descending is the same as ordering by instant.
    /// </summary>
    public IReadOnlyList<TimelineItem> LoadRecent(int limit, int offset = 0)
    {
        using var select = connection.CreateCommand();
        select.CommandText = $"""
            SELECT {SelectColumns} FROM timelineItems
            ORDER BY timestampUtc DESC
            LIMIT $limit OFFSET $offset
            """;
        select.Parameters.AddWithValue("$limit", limit);
        select.Parameters.AddWithValue("$offset", offset);
        return Read(select);
    }

    /// <summary>
    /// Brings a slice of the cache into line with an authoritative snapshot from
    /// the source (e.g. the current Gmail inbox+sent for the last N days): upserts
    /// everything in the snapshot, then removes any cached item of the same kind
    /// within [since, now] that the snapshot no longer contains — i.e. messages
    /// deleted at the source. Items older than <paramref name="since"/> are left
    /// alone, since the snapshot does not cover them. The caller must fetch the
    /// snapshot with the same (or an earlier) cutoff so the window is fully
    /// represented. Assumes UTC timestamps, matching the cache's writers.
    /// </summary>
    public void ReconcileWindow(
        TimelineItemKind kind, DateTimeOffset since, IReadOnlyCollection<TimelineItem> authoritative)
    {
        Upsert(authoritative);

        var sinceText = since.ToUniversalTime().ToString("O");
        using var transaction = connection.BeginTransaction();

        // Stage the surviving source ids in a temp table rather than a NOT IN
        // parameter list, which a full-window fetch could overflow.
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText =
                "CREATE TEMP TABLE IF NOT EXISTS reconcileKeep (sourceId TEXT PRIMARY KEY)";
            create.ExecuteNonQuery();
        }
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM reconcileKeep";
            clear.ExecuteNonQuery();
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO reconcileKeep (sourceId) VALUES ($sourceId)";
            var parameter = insert.CreateParameter();
            parameter.ParameterName = "$sourceId";
            insert.Parameters.Add(parameter);
            foreach (var item in authoritative)
            {
                parameter.Value = item.SourceId;
                insert.ExecuteNonQuery();
            }
        }
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM timelineItems
                WHERE kind = $kind
                  AND timestampUtc >= $since
                  AND sourceId NOT IN (SELECT sourceId FROM reconcileKeep)
                """;
            delete.Parameters.AddWithValue("$kind", (int)kind);
            delete.Parameters.AddWithValue("$since", sinceText);
            delete.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static List<TimelineItem> Read(SqliteCommand select)
    {
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
                MailClass = reader.IsDBNull(18) ? null : (Codex.Domain.Post.MailClass)reader.GetInt32(18),
                HtmlBody = ReadString(reader, 19),
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
