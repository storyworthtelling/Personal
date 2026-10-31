using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The <c>week-plan</c> record (D126): the kept week for the phone that does not hold the planning lease. Keyed by the week's Sunday. <b>Every field name and the format word are
/// frozen</b> (<c>WeekPlanMapTests</c> holds each as a literal): <c>format</c> ("1"), <c>weekStart</c> (yyyy-MM-dd), <c>planned</c> (round-trip instant), <c>week</c> (the week as
/// <see cref="MealWeekCodec"/> writes it: each evening's kind, card id, cook, cook block and reason), <c>dishes</c> (the card titles and markers), <c>deadline</c> (the instant silence starts to
/// approve), <c>extra</c> (the shopping lines a denied evening added) and <c>daily</c> (the template pages, or empty). Nothing private has a field: no birthdate, no key, no calendar entry.
///
/// <para><b>Read strictly.</b> A record in a newer format, or one whose fields do not say what a week needs, is set aside with a typed reason (<see cref="WeekPlanMaps.Read"/>) and never
/// guessed at; the sentence quotes nothing the record held. Only the lease holder's run writes one (<c>HouseholdWeekPlanPublisher</c>); it is merged by the library like any other record, so the newer
/// stamp wins on every phone.</para>
/// </summary>
internal sealed class WeekPlanMap : RecordMap<SharedWeekPlan>
{
    private const string Moments = "o";

    public override string Kind => ConstitutionKinds.WeekPlan;

    protected override string IdOf(SharedWeekPlan value) => RecordIds.Join(RecordDates.Write(value.WeekStart));

    public override IReadOnlyDictionary<string, string> FieldsOf(SharedWeekPlan value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Stored.Week.IsPlanned)
        {
            throw new ArgumentException("Only a planned week is shared; a week that was not planned would replace the last good one.", "value");
        }

        return new FieldWriter()
            .Text("format", WeekPlanMaps.FormatWord)
            .Date("weekStart", value.WeekStart)
            .Text("planned", value.Stored.PlannedAt.ToString(Moments, CultureInfo.InvariantCulture))
            .Text("week", MealWeekCodec.Write(value.Stored.Week))
            .Text("dishes", WriteDishes(value.Stored.Dishes))
            .Text("deadline", value.Deadline.ToString(Moments, CultureInfo.InvariantCulture))
            .Text("extra", WriteLines(value.ExtraLines))
            .Text("daily", value.Daily is null ? null : WriteDaily(value.Daily))
            .Fields;
    }

    public override SharedWeekPlan Read(RecordReader record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Text("format") != WeekPlanMaps.FormatWord)
        {
            throw record.Fail("format", "is a version this build does not read");
        }

        var start = record.OptionalDate("weekStart") ?? throw record.Fail("weekStart", "is missing");
        if (record.Key.Id != RecordIds.Join(RecordDates.Write(start)))
        {
            throw record.Fail("weekStart", "does not match the record's key");
        }

        var planned = Moment(record, "planned");
        var deadline = Moment(record, "deadline");
        var week = MealWeekCodec.Read(record.Text("week")) is MealWeekRead.Read read && read.Week.IsPlanned && read.Week.WeekStart == start
            ? read.Week
            : throw record.Fail("week", "cannot be read as this week");
        var dishes = Parsed(record, "dishes", ReadDishes, record.Text("dishes"));
        var extra = Parsed(record, "extra", ReadLines, record.Text("extra"));
        var dailyText = record.OptionalText("daily");
        var daily = dailyText is null ? null : Parsed(record, "daily", ReadDaily, dailyText);
        return new SharedWeekPlan(new StoredMealWeek(week, planned, dishes), deadline, extra, daily);
    }

    private static DateTimeOffset Moment(RecordReader record, string name) =>
        DateTimeOffset.TryParseExact(record.Text(name), Moments, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : throw record.Fail(name, "is not a moment");

    // A field that is not the shape asked for is the record's, never the reader's: the failure's own text can quote the field, so only its name is kept.
    private static T Parsed<T>(RecordReader record, string name, Func<string, T> read, string text)
    {
        try
        {
            return read(text);
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw record.Fail(name, "cannot be read");
        }
    }

    private static string Written(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteDishes(IReadOnlyList<StoredDish> dishes) => Written(writer =>
    {
        writer.WriteStartArray();
        foreach (var dish in dishes)
        {
            writer.WriteStartObject();
            writer.WriteString("recipe", dish.Recipe.Value);
            writer.WriteString("title", dish.Title);
            writer.WriteBoolean("slowCooker", dish.SlowCooker);
            writer.WriteBoolean("isNew", dish.IsNew);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });

    private static IReadOnlyList<StoredDish> ReadDishes(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.EnumerateArray().Select(dish => new StoredDish(
            new RecipeId(dish.GetProperty("recipe").GetString()!),
            dish.GetProperty("title").GetString() ?? throw new FormatException("title"),
            dish.GetProperty("slowCooker").GetBoolean(),
            dish.GetProperty("isNew").GetBoolean())).ToList();
    }

    private static string WriteLines(IReadOnlyList<string> lines) => Written(writer =>
    {
        writer.WriteStartArray();
        foreach (var line in lines)
        {
            writer.WriteStringValue(line);
        }

        writer.WriteEndArray();
    });

    private static IReadOnlyList<string> ReadLines(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.EnumerateArray().Select(line => line.GetString() ?? throw new FormatException("line")).ToList();
    }

    private static string WriteDaily(MealSheetDailyMeals daily) => Written(writer =>
    {
        writer.WriteStartObject();
        WriteSheetLines(writer, "weekdayBreakfasts", daily.WeekdayBreakfasts);
        WriteSheetLines(writer, "weekdayLunches", daily.WeekdayLunches);
        writer.WriteString("weekendBreakfast", daily.WeekendBreakfast);
        writer.WriteString("weekendLunch", daily.WeekendLunch);
        writer.WriteStartObject("lunchbox");
        writer.WriteString("sandwich", daily.Lunchbox.Sandwich);
        writer.WriteString("fruit", daily.Lunchbox.Fruit);
        writer.WriteString("snack", daily.Lunchbox.Snack);
        writer.WriteString("separateFruitOrVeg", daily.Lunchbox.SeparateFruitOrVeg);
        writer.WriteString("separateBar", daily.Lunchbox.SeparateBar);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static void WriteSheetLines(Utf8JsonWriter writer, string name, IReadOnlyList<MealSheetLine> lines)
    {
        writer.WriteStartArray(name);
        foreach (var line in lines)
        {
            writer.WriteStartObject();
            writer.WriteString("label", line.Label);
            writer.WriteString("text", line.Text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static MealSheetDailyMeals ReadDaily(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var box = root.GetProperty("lunchbox");
        return new MealSheetDailyMeals(
            ReadSheetLines(root.GetProperty("weekdayBreakfasts")),
            ReadSheetLines(root.GetProperty("weekdayLunches")),
            Word(root, "weekendBreakfast"),
            Word(root, "weekendLunch"),
            new MealSheetLunchbox(Word(box, "sandwich"), Word(box, "fruit"), Word(box, "snack"), Word(box, "separateFruitOrVeg"), Word(box, "separateBar")));
    }

    private static IReadOnlyList<MealSheetLine> ReadSheetLines(JsonElement array) =>
        array.EnumerateArray().Select(line => new MealSheetLine(Word(line, "label"), Word(line, "text"))).ToList();

    private static string Word(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new FormatException(name);
}

/// <summary>The one map for the <c>week-plan</c> record kind, and the one way to read every plan a household document holds.</summary>
public static class WeekPlanMaps
{
    /// <summary>The format word every record carries; a record with any other word is set aside as <see cref="WeekPlanSkipReason.UnknownVersion"/>.</summary>
    public const string FormatWord = "1";

    public static RecordMap<SharedWeekPlan> WeekPlan { get; } = new WeekPlanMap();

    /// <summary>
    /// Every live week-plan record, read. One in a newer format, or one that cannot be read, is reported in <see cref="WeekPlanSet.Skipped"/> with its typed reason and never
    /// guessed at; it does not stop the others. A deleted record is not here.
    /// </summary>
    public static WeekPlanSet Read(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var plans = new List<HeldWeekPlan>();
        var skipped = new List<WeekPlanSkipped>();
        foreach (var record in document.LiveRecords.Where(item => item.Key.Kind == ConstitutionKinds.WeekPlan))
        {
            if (record.Fields.TryGetValue("format", out var format) && format != FormatWord)
            {
                skipped.Add(new WeekPlanSkipped(record.Key, WeekPlanSkipReason.UnknownVersion));
                continue;
            }

            try
            {
                plans.Add(new HeldWeekPlan(WeekPlan.Read(new RecordReader(record)), record.Writer));
            }
            catch (HouseholdRecordException)
            {
                // Typed, not swallowed: the record is reported as skipped with its reason; the exception's own text is dropped because it names a field of a record this build could not read.
                skipped.Add(new WeekPlanSkipped(record.Key, WeekPlanSkipReason.Unreadable));
            }
        }

        return new WeekPlanSet(plans.OrderBy(item => item.Plan.WeekStart).ToList(), skipped);
    }
}
