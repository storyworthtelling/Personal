using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The documents waiting to be read, as a file: for each, its title parts, whose it is, and for each page the boxes the
/// painter drew (id, kind, place, meaning, unit, group) and the rules the book wrote for them. It is the shape of the
/// pages and never their content: no dose, no load, no voiced line, nothing the owner wrote. A rule or a box of a kind
/// this version does not know makes the whole file unreadable rather than being left out of a read.
/// </summary>
internal static class ExpectedDocumentCodec
{
    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";

    // A recipe's rating rows, stored as ids only (never a name or a dish). Frozen literals: a file already on a phone is read by
    // these words. Absent in a file written before the rows were carried, and then read as none.
    private const string RatingRowsName = "ratingRows";
    private const string RowGroupName = "group";
    private const string RowPersonName = "person";
    private const string RowRecipeName = "recipe";

    public static string Encode(IEnumerable<OutstandingDocument> documents)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("documents");
            foreach (var item in documents)
            {
                WriteDocument(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a file written by <see cref="Encode"/>; anything else is a <see cref="FormatException"/> with the cause kept.</summary>
    public static IReadOnlyList<OutstandingDocument> Decode(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException("This manifest is of a version this app does not read.");
            }

            return root.GetProperty("documents").EnumerateArray().Select(ReadDocument).ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("The manifest could not be read as the documents waiting for their ink.", e);
        }
    }

    // ---- writing ----

    private static void WriteDocument(Utf8JsonWriter writer, OutstandingDocument item)
    {
        var document = item.Document;
        writer.WriteStartObject();
        writer.WriteString("kind", ReadBackWireNames.Of(document.Kind));
        writer.WriteString("day", document.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
        writer.WriteString("subject", document.Subject);
        writer.WriteString("person", document.Person);
        writer.WriteString("session", document.Session is { } session ? ReadBackWireNames.Of(session) : null);
        writer.WriteBoolean("saved", item.Saved);
        writer.WriteStartArray("pages");
        foreach (var page in document.Pages)
        {
            writer.WriteStartObject();
            writer.WriteNumber("number", page.Number);
            writer.WriteStartArray("targets");
            foreach (var target in page.Readout.Map.Targets)
            {
                WriteTarget(writer, target);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("rules");
            foreach (var rule in page.Rules)
            {
                WriteRule(writer, rule);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (document.RatingRows.Count > 0)
        {
            writer.WriteStartArray(RatingRowsName);
            foreach (var row in document.RatingRows)
            {
                writer.WriteStartObject();
                writer.WriteString(RowGroupName, row.Group);
                writer.WriteString(RowPersonName, row.Person.Value);
                writer.WriteString(RowRecipeName, row.Recipe.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteTarget(Utf8JsonWriter writer, InkTarget target)
    {
        writer.WriteStartObject();
        writer.WriteString("id", target.Id);
        writer.WriteString("kind", ReadBackWireNames.Of(target.Kind));
        writer.WriteNumber("left", target.Bounds.Left);
        writer.WriteNumber("top", target.Bounds.Top);
        writer.WriteNumber("width", target.Bounds.Width);
        writer.WriteNumber("height", target.Bounds.Height);
        writer.WriteString("meaning", target.Meaning);
        writer.WriteString("unit", target.Unit);
        writer.WriteString("group", target.Group);
        writer.WriteEndObject();
    }

    private static void WriteRule(Utf8JsonWriter writer, InkRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("subject", rule.Subject);
        switch (rule)
        {
            case WorkoutBoxRule r:
                writer.WriteString("type", "workout-box");
                writer.WriteString("box", r.BoxId);
                break;
            case ExercisePageRule r:
                writer.WriteString("type", "exercise-page");
                writer.WriteString("done", r.DoneBoxId);
                writer.WriteString("rating", r.RatingGroup);
                break;
            case MealItemRule r:
                writer.WriteString("type", "meal-item");
                writer.WriteString("yes", r.YesBoxId);
                writer.WriteString("reason", r.ReasonId);
                writer.WriteString("deadline", r.Deadline.ToString("O", CultureInfo.InvariantCulture));
                break;
            case RatingRule r:
                writer.WriteString("type", "rating");
                writer.WriteString("group", r.Group);
                break;
            case StillSoreRule r:
                writer.WriteString("type", "still-sore");
                writer.WriteString("box", r.BoxId);
                break;
            case StapleRule r:
                writer.WriteString("type", "staple");
                writer.WriteString("line", r.LineId);
                break;
            case NumberRule r:
                writer.WriteString("type", "number");
                writer.WriteString("box", r.BoxId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rule), rule, "This kind of rule has no stored word; add one to ExpectedDocumentCodec.");
        }

        writer.WriteEndObject();
    }

    // ---- reading ----

    private static OutstandingDocument ReadDocument(JsonElement item)
    {
        var kind = ReadBackWireNames.DeliveryKindOf(Text(item, "kind"))
            ?? throw new FormatException("The manifest names a document kind this app does not know.");
        SessionKind? session = null;
        if (Optional(item, "session") is { } word)
        {
            session = ReadBackWireNames.SessionKindOf(word) ?? throw new FormatException("The manifest names a session kind this app does not know.");
        }

        var pages = item.GetProperty("pages").EnumerateArray().Select(ReadPage).ToList();
        var document = new ExpectedDocument(
            kind, DateOnly.ParseExact(Text(item, "day"), DayFormat, CultureInfo.InvariantCulture),
            Optional(item, "subject"), Text(item, "person"), session, pages, ReadRatingRows(item));
        return new OutstandingDocument(document, item.GetProperty("saved").GetBoolean());
    }

    private static List<TasteRatingRow> ReadRatingRows(JsonElement item)
    {
        if (!item.TryGetProperty(RatingRowsName, out var rows))
        {
            return [];
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The manifest's rating rows are not a list.");
        }

        return rows.EnumerateArray()
            .Select(row => new TasteRatingRow(Text(row, RowGroupName), new PersonId(Text(row, RowPersonName)), new RecipeId(Text(row, RowRecipeName))))
            .ToList();
    }

    private static ExpectedPage ReadPage(JsonElement page)
    {
        var map = new InkMap(page.GetProperty("targets").EnumerateArray().Select(ReadTarget));
        var rules = page.GetProperty("rules").EnumerateArray().Select(ReadRule).ToList();
        return new ExpectedPage(page.GetProperty("number").GetInt32(), new InkReadout(map), rules);
    }

    private static InkTarget ReadTarget(JsonElement target)
    {
        var kind = ReadBackWireNames.InkTargetKindOf(Text(target, "kind"))
            ?? throw new FormatException("The manifest names a box kind this app does not know.");

        var bounds = new InkRect(
            target.GetProperty("left").GetSingle(), target.GetProperty("top").GetSingle(),
            target.GetProperty("width").GetSingle(), target.GetProperty("height").GetSingle());
        return new InkTarget(Text(target, "id"), kind, bounds, Text(target, "meaning"), Optional(target, "unit"), Optional(target, "group"));
    }

    private static InkRule ReadRule(JsonElement rule)
    {
        var subject = Text(rule, "subject");
        return Text(rule, "type") switch
        {
            "workout-box" => new WorkoutBoxRule(subject, Text(rule, "box")),
            "exercise-page" => new ExercisePageRule(subject, Text(rule, "done"), Text(rule, "rating")),
            "meal-item" => new MealItemRule(
                subject, Text(rule, "yes"), Text(rule, "reason"),
                DateTimeOffset.Parse(Text(rule, "deadline"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            "rating" => new RatingRule(subject, Text(rule, "group")),
            "still-sore" => new StillSoreRule(subject, Text(rule, "box")),
            "staple" => new StapleRule(subject, Text(rule, "line")),
            "number" => new NumberRule(subject, Text(rule, "box")),
            _ => throw new FormatException("The manifest holds a kind of rule this app does not know."),
        };
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new FormatException($"The manifest has no text called '{name}' where one belongs.");

    private static string? Optional(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
