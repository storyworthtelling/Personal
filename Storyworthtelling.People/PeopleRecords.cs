using System.Globalization;
using Storyworthtelling.Household;

namespace Storyworthtelling.People;

/// <summary>
/// How a person and the rhythm are kept as record fields. The field names are <b>frozen</b> (the file outlives every build); a field this
/// build does not know is carried through untouched by <see cref="HouseholdRecord.Edit"/>, and an absent optional value is written as an
/// empty string so an edit clears it.
/// </summary>
internal static class PeopleRecords
{
    public const int SchemaVersion = 1;

    public const string LinkPrefix = "link.";

    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    public const string RhythmId = "household";

    public static RecordKey KeyOf(string personId) => new(PeopleKinds.Person, personId);

    public static RecordKey RhythmKey { get; } = new(PeopleKinds.Rhythm, RhythmId);

    public static Dictionary<string, string> FieldsOf(HouseholdPerson person)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = person.Name,
            ["child"] = Flag(person.IsChild),
            ["birthdate"] = person.Birthdate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
            ["pickup"] = Flag(person.OnPickup),
            ["google-address"] = person.GoogleAddress ?? string.Empty,
            ["calendar"] = person.CalendarId ?? string.Empty,
        };
        foreach (var link in person.Links)
        {
            fields[LinkPrefix + link.Key] = link.Value;
        }

        return fields;
    }

    /// <summary>The person a live record holds; throws <see cref="FormatException"/> naming the field (never its value) when it cannot be read.</summary>
    public static HouseholdPerson ReadPerson(HouseholdRecord record)
    {
        var fields = record.Fields;
        var links = fields.Where(pair => pair.Key.StartsWith(LinkPrefix, StringComparison.Ordinal) && pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key[LinkPrefix.Length..], pair => pair.Value, StringComparer.Ordinal);
        try
        {
            return new HouseholdPerson(
                record.Key.Id,
                Text(fields, "name") ?? throw new FormatException("the person has no name"),
                ReadFlag(fields, "child"),
                Text(fields, "birthdate") is { } date ? ParseDate(date) : null,
                ReadFlag(fields, "pickup"),
                Text(fields, "google-address"),
                Text(fields, "calendar"))
            {
                Links = links,
            };
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("the person breaks a household rule (" + exception.ParamName + ")", exception);
        }
    }

    public static Dictionary<string, string> FieldsOf(HouseholdRhythm rhythm) => new(StringComparer.Ordinal)
    {
        ["family-calendar"] = rhythm.FamilyCalendarId ?? string.Empty,
        ["no-family-calendar"] = Flag(rhythm.NoFamilyCalendar),
        ["dinner-start"] = Time(rhythm.DinnerStart),
        ["dinner-minutes"] = rhythm.DinnerLength is { } length ? ((int)length.TotalMinutes).ToString(CultureInfo.InvariantCulture) : string.Empty,
        ["bedtime"] = Time(rhythm.Bedtime),
        ["zone"] = rhythm.ZoneId ?? string.Empty,
    };

    public static HouseholdRhythm ReadRhythm(HouseholdRecord record)
    {
        var fields = record.Fields;
        return new HouseholdRhythm
        {
            FamilyCalendarId = Text(fields, "family-calendar"),
            NoFamilyCalendar = Text(fields, "no-family-calendar") == "true",
            DinnerStart = Text(fields, "dinner-start") is { } start ? ParseTime(start) : null,
            DinnerLength = Text(fields, "dinner-minutes") is { } minutes && int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? TimeSpan.FromMinutes(count)
                : null,
            Bedtime = Text(fields, "bedtime") is { } bedtime ? ParseTime(bedtime) : null,
            ZoneId = Text(fields, "zone"),
        };
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static bool ReadFlag(IReadOnlyDictionary<string, string> fields, string name) => Text(fields, name) switch
    {
        null or "false" => false,
        "true" => true,
        _ => throw new FormatException("the field '" + name + "' isn’t true or false"),
    };

    private static string? Text(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string Time(TimeOnly? time) => time?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException("the birthdate isn’t a date");

    private static TimeOnly? ParseTime(string text) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
}
