using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The decided weeks as JSON: <c>{"version":1,"decisions":[{"week","decidedAt","approved","denied","approvedUnread","needsRevisedSheet","replanWithout":[card ids],
/// "evidencePending"}]}</c>. The names are frozen. A text that is not this record is unreadable with a fixed sentence quoting nothing, never an empty record.
/// </summary>
public static class MealDecisionRecordCodec
{
    public const int Version = 1;
    public const string NotJson = "The saved meal decisions are not valid JSON.";
    public const string WrongShape = "The saved meal decisions are not in a shape this version reads.";

    public static string Encode(MealDecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("decisions");
            foreach (var entry in record.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("week", entry.Week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteString("decidedAt", entry.DecidedAt.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteNumber("approved", entry.Approved);
                writer.WriteNumber("denied", entry.Denied);
                writer.WriteNumber("approvedUnread", entry.ApprovedUnread);
                writer.WriteBoolean("needsRevisedSheet", entry.NeedsRevisedSheet);
                writer.WriteStartArray("replanWithout");
                foreach (var card in entry.ReplanWithout)
                {
                    writer.WriteStringValue(card.Value);
                }

                writer.WriteEndArray();
                writer.WriteBoolean("evidencePending", entry.EvidencePending);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static MealDecisionRecordRead Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MealDecisionRecordRead.NothingYet();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Nothing from the exception is kept: its text can quote the document.
            return new MealDecisionRecordRead.Unreadable(NotJson);
        }

        using (document)
        {
            try
            {
                return new MealDecisionRecordRead.Held(FromRoot(document.RootElement));
            }
            catch (Exception failure) when (failure is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                // A typed result with a fixed sentence: the failure's own text may quote the document.
                return new MealDecisionRecordRead.Unreadable(WrongShape);
            }
        }
    }

    private static MealDecisionRecord FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != Version)
        {
            throw new FormatException("version");
        }

        return root.GetProperty("decisions").EnumerateArray().Select(EntryFrom).Aggregate(MealDecisionRecord.Empty, (record, entry) => record.With(entry));
    }

    private static MealDecisionEntry EntryFrom(JsonElement element)
    {
        var week = MealWeekCodec.ParseDay(element.GetProperty("week").GetString());
        PlanningWeek.RequireStart(week, "week");
        var decided = DateTimeOffset.TryParseExact(element.GetProperty("decidedAt").GetString(), "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : throw new FormatException("decidedAt");
        return new MealDecisionEntry(
            week,
            decided,
            element.GetProperty("approved").GetInt32(),
            element.GetProperty("denied").GetInt32(),
            element.GetProperty("approvedUnread").GetInt32(),
            element.GetProperty("needsRevisedSheet").GetBoolean(),
            element.GetProperty("replanWithout").EnumerateArray().Select(card => new RecipeId(card.GetString() ?? throw new FormatException("replanWithout"))).ToList(),
            element.GetProperty("evidencePending").GetBoolean());
    }
}
