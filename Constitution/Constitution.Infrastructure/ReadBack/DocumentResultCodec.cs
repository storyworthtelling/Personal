using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// A document's result as a file: every page's resolutions, each by a fixed word for its kind, and the owner's pick for
/// each question they have answered. A resolution of a kind this version does not know is not skipped (it would be an
/// answer silently dropped); the whole file is unreadable and is set aside.
/// </summary>
internal static class DocumentResultCodec
{
    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";

    public sealed record Stored(DocumentResult Result, IReadOnlyDictionary<string, string> Answered);

    public static string Encode(DocumentResult result, IReadOnlyDictionary<string, string> answered)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("name", result.Name);
            writer.WriteString("kind", result.Kind.ToString());
            writer.WriteString("day", result.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
            writer.WriteString("person", result.Person);
            writer.WriteString("readAt", result.ReadAt.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartArray("answered");
            foreach (var (cause, chosen) in answered)
            {
                writer.WriteStartObject();
                writer.WriteString("cause", cause);
                writer.WriteString("chosen", chosen);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("pages");
            foreach (var page in result.Pages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", page.Number);
                writer.WriteStartArray("resolutions");
                foreach (var resolution in page.Resolutions)
                {
                    WriteResolution(writer, resolution);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a file written by <see cref="Encode"/>; anything else is a <see cref="FormatException"/> with the cause kept.</summary>
    public static Stored Decode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException("This result file is of a version this app does not read.");
            }

            var kind = Enum.Parse<DeliveryKind>(Text(root, "kind"));
            if (!Enum.IsDefined(kind))
            {
                throw new FormatException("This result file names a document kind this app does not know.");
            }

            var pages = root.GetProperty("pages").EnumerateArray()
                .Select(page => new PageResolutions(
                    page.GetProperty("number").GetInt32(),
                    page.GetProperty("resolutions").EnumerateArray().Select(ReadResolution).ToList()))
                .ToList();
            var result = new DocumentResult(
                Text(root, "name"),
                kind,
                DateOnly.ParseExact(Text(root, "day"), DayFormat, CultureInfo.InvariantCulture),
                Text(root, "person"),
                DateTimeOffset.Parse(Text(root, "readAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                pages);
            var answered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pick in root.GetProperty("answered").EnumerateArray())
            {
                answered[Text(pick, "cause")] = Text(pick, "chosen");
            }

            return new Stored(result, answered);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("The result file could not be read as a document's result.", e);
        }
    }

    private static void WriteResolution(Utf8JsonWriter writer, InkResolution resolution)
    {
        writer.WriteStartObject();
        writer.WriteString("id", resolution.SubjectId);
        switch (resolution)
        {
            case InkResolution.Ticked:
                writer.WriteString("type", "ticked");
                break;
            case InkResolution.Skipped:
                writer.WriteString("type", "skipped");
                break;
            case InkResolution.Denied denied:
                writer.WriteString("type", "denied");
                writer.WriteString("reason", denied.Reason);
                break;
            case InkResolution.ApprovedAtDeadline:
                writer.WriteString("type", "approved-at-deadline");
                break;
            case InkResolution.Unchanged:
                writer.WriteString("type", "unchanged");
                break;
            case InkResolution.Better:
                writer.WriteString("type", "better");
                break;
            case InkResolution.StillSore:
                writer.WriteString("type", "still-sore");
                break;
            case InkResolution.NoNeed:
                writer.WriteString("type", "no-need");
                break;
            case InkResolution.Needed needed:
                writer.WriteString("type", "needed");
                writer.WriteString("text", needed.Text);
                break;
            case InkResolution.Circled circled:
                writer.WriteString("type", "circled");
                writer.WriteString("word", circled.Word);
                break;
            case InkResolution.Written written:
                writer.WriteString("type", "written");
                writer.WriteString("text", written.Text);
                break;
            case InkResolution.PageDone pageDone:
                writer.WriteString("type", "page-done");
                writer.WriteString("rating", pageDone.Rating);
                break;
            case InkResolution.NeedsAsking asking:
                writer.WriteString("type", "needs-asking");
                writer.WriteString("subject", asking.Subject);
                writer.WriteString("first", asking.First);
                writer.WriteString("second", asking.Second);
                writer.WriteString("cause", asking.CauseKey);
                break;
            case InkResolution.Undecided:
                writer.WriteString("type", "undecided");
                break;
            case InkResolution.NotRead notRead:
                writer.WriteString("type", "not-read");
                writer.WriteString("why", notRead.Why);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "This kind of resolution has no stored word; add one to DocumentResultCodec.");
        }

        writer.WriteEndObject();
    }

    private static InkResolution ReadResolution(JsonElement item)
    {
        var id = Text(item, "id");
        return item.GetProperty("type").GetString() switch
        {
            "ticked" => new InkResolution.Ticked(id),
            "skipped" => new InkResolution.Skipped(id),
            "denied" => new InkResolution.Denied(id, Optional(item, "reason")),
            "approved-at-deadline" => new InkResolution.ApprovedAtDeadline(id),
            "unchanged" => new InkResolution.Unchanged(id),
            "better" => new InkResolution.Better(id),
            "still-sore" => new InkResolution.StillSore(id),
            "no-need" => new InkResolution.NoNeed(id),
            "needed" => new InkResolution.Needed(id, Optional(item, "text")),
            "circled" => new InkResolution.Circled(id, Text(item, "word")),
            "written" => new InkResolution.Written(id, Text(item, "text")),
            "page-done" => new InkResolution.PageDone(id, Text(item, "rating")),
            "needs-asking" => new InkResolution.NeedsAsking(
                id, Text(item, "subject"), Text(item, "first"),
                Text(item, "second"), Text(item, "cause")),
            "undecided" => new InkResolution.Undecided(id),
            "not-read" => new InkResolution.NotRead(id, Text(item, "why")),
            _ => throw new FormatException("This result holds a kind of answer this app does not know."),
        };
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new FormatException($"The result file has no text called '{name}' where one belongs.");

    private static string? Optional(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
