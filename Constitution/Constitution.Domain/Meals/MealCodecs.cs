using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>What reading a stored week gave: the week, or a plain reason it cannot be used. The reason quotes nothing from the document.</summary>
public abstract record MealWeekRead
{
    public sealed record Read(MealWeek Week) : MealWeekRead;

    public sealed record Unreadable(string Reason) : MealWeekRead;
}

/// <summary>
/// The week as JSON: <c>{"version":1,"weekStart":"yyyy-MM-dd","stop":null|word,"stopReason":…,"notes":[…],"dinners":[{"day","kind",
/// "recipe","cook","cookStart","cookEnd","stretch","childrenDifferent","reason"}]}</c>. Words are frozen literals; times keep their
/// offset; unknown keys are ignored. The week has no origin to store.
/// </summary>
public static class MealWeekCodec
{
    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";

    public const string NotJson = "The saved meal week is not valid JSON.";
    public const string WrongShape = "The saved meal week is not in a shape this version reads.";

    public static string Write(MealWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("weekStart", Day(week.WeekStart));
            if (week.Stop is { } stop)
            {
                writer.WriteString("stop", StopWord(stop));
                writer.WriteString("stopReason", week.StopReason);
            }

            writer.WriteStartArray("notes");
            foreach (var note in week.Notes)
            {
                writer.WriteStringValue(note);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("dinners");
            foreach (var dinner in week.Dinners)
            {
                writer.WriteStartObject();
                writer.WriteString("day", Day(dinner.Day));
                writer.WriteString("kind", KindWord(dinner.Kind));
                if (dinner.Kind == DinnerKind.Cooked)
                {
                    writer.WriteString("recipe", dinner.Recipe!.Value.Value);
                    writer.WriteString("cook", dinner.Cook);
                    writer.WriteString("cookStart", dinner.CookStart!.Value.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteString("cookEnd", dinner.CookEnd!.Value.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteBoolean("stretch", dinner.IsStretch);
                    writer.WriteBoolean("childrenDifferent", dinner.ChildrenEatDifferently);
                }

                writer.WriteString("reason", dinner.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static MealWeekRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MealWeekRead.Unreadable(WrongShape);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Nothing from the exception is kept: its text can quote the document.
            return new MealWeekRead.Unreadable(NotJson);
        }

        using (document)
        {
            try
            {
                return new MealWeekRead.Read(FromRoot(document.RootElement));
            }
            catch (Exception failure) when (failure is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                // A typed result with a fixed sentence: the failure's own text may quote the document.
                return new MealWeekRead.Unreadable(WrongShape);
            }
        }
    }

    private static MealWeek FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != Version)
        {
            throw new FormatException("version");
        }

        var weekStart = ParseDay(root.GetProperty("weekStart").GetString());
        var notes = root.TryGetProperty("notes", out var noteList) ? noteList.EnumerateArray().Select(note => note.GetString() ?? throw new FormatException("note")).ToList() : [];
        if (root.TryGetProperty("stop", out var stop) && stop.ValueKind == JsonValueKind.String)
        {
            return MealWeek.NotPlanned(weekStart, StopFrom(stop.GetString()), root.GetProperty("stopReason").GetString() ?? throw new FormatException("stopReason"));
        }

        var dinners = root.GetProperty("dinners").EnumerateArray().Select(DinnerFrom).ToList();
        return MealWeek.Planned(weekStart, dinners, notes);
    }

    private static DinnerChoice DinnerFrom(JsonElement element)
    {
        var day = ParseDay(element.GetProperty("day").GetString());
        var reason = element.GetProperty("reason").GetString() ?? throw new FormatException("reason");
        return KindFrom(element.GetProperty("kind").GetString()) switch
        {
            DinnerKind.Cooked => DinnerChoice.Cooked(
                day,
                new RecipeId(element.GetProperty("recipe").GetString()!),
                element.GetProperty("cook").GetString()!,
                ParseTime(element.GetProperty("cookStart").GetString()),
                ParseTime(element.GetProperty("cookEnd").GetString()),
                Flag(element, "stretch"),
                Flag(element, "childrenDifferent"),
                reason),
            DinnerKind.Leftovers => DinnerChoice.Leftovers(day, reason),
            DinnerKind.EatOut => DinnerChoice.EatOut(day, reason),
            _ => DinnerChoice.NothingPlanned(day, reason),
        };
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static string Day(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    internal static DateOnly ParseDay(string? text) =>
        DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : throw new FormatException("day");

    private static DateTimeOffset ParseTime(string? text) =>
        DateTimeOffset.TryParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : throw new FormatException("time");

    private static string KindWord(DinnerKind kind) => kind switch
    {
        DinnerKind.Cooked => "cooked",
        DinnerKind.Leftovers => "leftovers",
        DinnerKind.EatOut => "eatOut",
        DinnerKind.NothingPlanned => "nothingPlanned",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No word for this kind."),
    };

    private static DinnerKind KindFrom(string? word) => word switch
    {
        "cooked" => DinnerKind.Cooked,
        "leftovers" => DinnerKind.Leftovers,
        "eatOut" => DinnerKind.EatOut,
        "nothingPlanned" => DinnerKind.NothingPlanned,
        _ => throw new FormatException("kind"),
    };

    private static string StopWord(MealStop stop) => stop switch
    {
        MealStop.BoxTooSmall => "boxTooSmall",
        MealStop.CalendarUnreadable => "calendarUnreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(stop), stop, "No word for this stop."),
    };

    private static MealStop StopFrom(string? word) => word switch
    {
        "boxTooSmall" => MealStop.BoxTooSmall,
        "calendarUnreadable" => MealStop.CalendarUnreadable,
        _ => throw new FormatException("stop"),
    };
}

/// <summary>What reading the stored history gave. A history that cannot be used is not an empty one: the head must not plan on a memory it lost.</summary>
public sealed record MealHistoryRead(IReadOnlyList<CookedMeal> Meals, string? UnreadableBecause)
{
    public bool IsReadable => UnreadableBecause is null;
}

/// <summary>The recent history as JSON: <c>{"version":1,"meals":[{"recipe":"id","day":"yyyy-MM-dd"}]}</c>.</summary>
public static class MealHistoryCodec
{
    private const int Version = 1;

    public const string Unusable = "The saved dinner history is not in a shape this version reads.";

    public static string Write(IEnumerable<CookedMeal> meals)
    {
        ArgumentNullException.ThrowIfNull(meals);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("meals");
            foreach (var meal in meals)
            {
                writer.WriteStartObject();
                writer.WriteString("recipe", meal.Recipe.Value);
                writer.WriteString("day", meal.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static MealHistoryRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MealHistoryRead([], Unusable);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != Version)
            {
                return new MealHistoryRead([], Unusable);
            }

            var meals = root.GetProperty("meals").EnumerateArray()
                .Select(element => new CookedMeal(new RecipeId(element.GetProperty("recipe").GetString()!), MealWeekCodec.ParseDay(element.GetProperty("day").GetString())))
                .ToList();
            return new MealHistoryRead(meals, null);
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            // A typed result with a fixed sentence: the failure's own text may quote the document.
            return new MealHistoryRead([], Unusable);
        }
    }
}
