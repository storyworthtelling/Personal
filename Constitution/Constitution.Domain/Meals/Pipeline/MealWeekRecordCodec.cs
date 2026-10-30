using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The saved weeks as JSON: <c>{"version":1,"weeks":[{"weekStart","plannedAt","week":{the frozen week of <see cref="MealWeekCodec"/>},
/// "dishes":[{"recipe","title","slowCooker","isNew"}]}]}</c>. The names are frozen (a guard reads the text). A text that is not this
/// record is unreadable with a fixed sentence quoting nothing, never an empty record: an empty one would read as "nothing is planned".
/// </summary>
public static class MealWeekRecordCodec
{
    public const int Version = 1;
    public const string NotJson = "The saved meal weeks are not valid JSON.";
    public const string WrongShape = "The saved meal weeks are not in a shape this version reads.";

    public static string Encode(MealWeekRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("weeks");
            foreach (var stored in record.Weeks)
            {
                writer.WriteStartObject();
                writer.WriteString("weekStart", stored.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteString("plannedAt", stored.PlannedAt.ToString("o", CultureInfo.InvariantCulture));
                writer.WritePropertyName("week");
                writer.WriteRawValue(MealWeekCodec.Write(stored.Week));
                writer.WriteStartArray("dishes");
                foreach (var dish in stored.Dishes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("recipe", dish.Recipe.Value);
                    writer.WriteString("title", dish.Title);
                    writer.WriteBoolean("slowCooker", dish.SlowCooker);
                    writer.WriteBoolean("isNew", dish.IsNew);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static MealWeekRecordRead Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MealWeekRecordRead.NothingYet();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Nothing from the exception is kept: its text can quote the document.
            return new MealWeekRecordRead.Unreadable(NotJson);
        }

        using (document)
        {
            try
            {
                return new MealWeekRecordRead.Held(FromRoot(document.RootElement));
            }
            catch (Exception failure) when (failure is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                // A typed result with a fixed sentence: the failure's own text may quote the document.
                return new MealWeekRecordRead.Unreadable(WrongShape);
            }
        }
    }

    /// <summary>The week as text without the moment it was first planned: two weeks that say the same thing have the same key.</summary>
    public static string ContentKey(StoredMealWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        return Encode(new MealWeekRecord([week with { PlannedAt = DateTimeOffset.MinValue }]));
    }

    private static MealWeekRecord FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != Version)
        {
            throw new FormatException("version");
        }

        var weeks = root.GetProperty("weeks").EnumerateArray().Select(WeekFrom).ToList();
        return weeks.Aggregate(MealWeekRecord.Empty, (record, week) => record.With(week));
    }

    private static StoredMealWeek WeekFrom(JsonElement element)
    {
        var start = MealWeekCodec.ParseDay(element.GetProperty("weekStart").GetString());
        var planned = DateTimeOffset.TryParseExact(element.GetProperty("plannedAt").GetString(), "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : throw new FormatException("plannedAt");
        var week = MealWeekCodec.Read(element.GetProperty("week").GetRawText()) is MealWeekRead.Read read && read.Week.IsPlanned && read.Week.WeekStart == start
            ? read.Week
            : throw new FormatException("week");
        var dishes = element.GetProperty("dishes").EnumerateArray().Select(dish => new StoredDish(
            new RecipeId(dish.GetProperty("recipe").GetString()!),
            dish.GetProperty("title").GetString() ?? throw new FormatException("title"),
            Flag(dish, "slowCooker"),
            Flag(dish, "isNew"))).ToList();
        return new StoredMealWeek(week, planned, dishes);
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
