using System.Text.Encodings.Web;
using System.Text.Json;
using Codex.Domain.Timeline;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>The book as it was stored, or that it was there and would not read (<paramref name="Unread"/>).</summary>
public sealed record DashActsRead(IReadOnlyList<DashActRecord> Records, bool Unread);

/// <summary>
/// The book of the wrist's acts as Post keeps it on the phone (P14 B2), so it outlives the process: UTF-8 JSON with an
/// integer <c>v</c>, each act by the watch's own name, its kind, until when it can be taken back, whether it was taken
/// back or carried out, how a send went, and the messages it took (kind as the timeline cache stores it, source id, and a
/// mail's message id). The words are frozen, since a renamed one would turn the book into a new one. A book that will not
/// read, or a newer one, is not read: the relay starts a new book rather than stopping, and says so once.
/// </summary>
public static class DashActsCodec
{
    public const int Version = 1;

    // Stored words, frozen.
    private const string VersionField = "v";
    private const string ActsField = "acts";
    private const string ActField = "act";
    private const string KindField = "kind";
    private const string UntilField = "until";
    private const string TakenField = "taken";
    private const string DoneField = "done";
    private const string ItemsField = "items";
    private const string SentField = "sent";
    private const string SourceField = "source";
    private const string MessageField = "message";

    // In DashActKind's order.
    private static readonly string[] KindWords = ["file", "delete", "send"];

    // The latest instant a DateTimeOffset holds, in Unix milliseconds.
    private const long LastInstant = 253_402_300_799_999;

    private static readonly JsonWriterOptions Writing = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Encode(IReadOnlyList<DashActRecord> records)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json, Writing))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, Version);
            writer.WritePropertyName(ActsField);
            writer.WriteStartArray();
            foreach (var record in records)
            {
                writer.WriteStartObject();
                writer.WriteString(ActField, record.Act);
                writer.WriteString(KindField, KindWords[(int)record.Kind]);
                writer.WriteNumber(UntilField, record.Until.ToUnixTimeMilliseconds());
                writer.WriteBoolean(TakenField, record.Taken);
                writer.WriteBoolean(DoneField, record.Done);
                writer.WritePropertyName(ItemsField);
                writer.WriteStartArray();
                foreach (var item in record.Items)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(KindField, (int)item.Kind);
                    writer.WriteString(SourceField, item.SourceId);
                    if (item.MessageId is not null)
                    {
                        writer.WriteString(MessageField, item.MessageId);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (record.Sent is { } sent)
                {
                    writer.WriteString(SentField, DashCodec.Word(sent));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    public static DashActsRead Decode(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return new DashActsRead([], Unread: false);
        }

        try
        {
            using var document = JsonDocument.Parse(stored);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(VersionField, out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != Version
                || !root.TryGetProperty(ActsField, out var acts) || acts.ValueKind != JsonValueKind.Array)
            {
                return Unread();
            }

            var records = new List<DashActRecord>();
            foreach (var entry in acts.EnumerateArray())
            {
                if (Read(entry) is not { } record)
                {
                    return Unread();
                }
                records.Add(record);
            }
            return new DashActsRead(records, Unread: false);
        }
        catch (JsonException)
        {
            // Not JSON: nothing in it can be trusted, so the book starts again and the head says so.
            return Unread();
        }
    }

    private static DashActRecord? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !Text(entry, ActField, out var act) || act.Length == 0
            || !Text(entry, KindField, out var kindWord) || Array.IndexOf(KindWords, kindWord) < 0
            || !Instant(entry, UntilField, out var until)
            || !Flag(entry, TakenField, out var taken)
            || !Flag(entry, DoneField, out var done)
            || !entry.TryGetProperty(ItemsField, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        DashSendOutcome? sent = null;
        if (entry.TryGetProperty(SentField, out var said))
        {
            if (said.ValueKind != JsonValueKind.String
                || Enum.GetValues<DashSendOutcome>().Where(outcome => DashCodec.Word(outcome) == said.GetString()).ToList() is not [var outcome])
            {
                return null;
            }
            sent = outcome;
        }

        var acted = new List<ActedItem>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(KindField, out var itemKind) || itemKind.ValueKind != JsonValueKind.Number
                || !itemKind.TryGetInt32(out var kindNumber) || !Enum.IsDefined((TimelineItemKind)kindNumber)
                || !Text(item, SourceField, out var source) || source.Length == 0)
            {
                return null;
            }

            string? message = null;
            if (item.TryGetProperty(MessageField, out var messageId))
            {
                if (messageId.ValueKind != JsonValueKind.String)
                {
                    return null;
                }
                message = messageId.GetString();
            }
            acted.Add(new ActedItem((TimelineItemKind)kindNumber, source, message));
        }

        return new DashActRecord(act, (DashActKind)Array.IndexOf(KindWords, kindWord), acted, until, taken, done, sent);
    }

    private static bool Text(JsonElement entry, string field, out string value)
    {
        value = string.Empty;
        if (!entry.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = said.GetString() ?? string.Empty;
        return true;
    }

    private static bool Flag(JsonElement entry, string field, out bool value)
    {
        value = false;
        if (!entry.TryGetProperty(field, out var said) || said.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }
        value = said.GetBoolean();
        return true;
    }

    private static bool Instant(JsonElement entry, string field, out DateTimeOffset value)
    {
        value = default;
        if (!entry.TryGetProperty(field, out var said) || said.ValueKind != JsonValueKind.Number
            || !said.TryGetInt64(out var milliseconds) || milliseconds < 0 || milliseconds > LastInstant)
        {
            return false;
        }
        value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }

    private static DashActsRead Unread() => new([], Unread: true);
}
