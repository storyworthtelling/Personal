using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Constitution.Domain.Calendar;

/// <summary>
/// The household's setup as one small JSON file in the phone's own storage. Read tolerantly and never thrown from: a file that cannot be
/// used is <see cref="HouseholdSetupRead.NotSet"/> with a plain sentence, and the sentence quotes nothing from the document (it holds
/// addresses). The shape is
/// <c>{"version":1,"zone":"…","adults":[{"name","calendar","pickup","addresses":[…],"otherNames":[…],"deliversHere"}],
/// "familyCalendar":"…","noFamilyCalendar":false,"childrenCalendars":[…],"noChildren":false,"dinner":"HH:mm","dinnerMinutes":45,"bedtime":"HH:mm"}</c>,
/// keys matched without case, every key but the adults optional.
///
/// <para><b>A bad structure is not set; a bad setting is not given.</b> An adult with no name, two with one name, or no adults at all
/// leave nothing to plan for. A dinner time that is not a time, or a length that makes no sense, is simply a setting the household has not given,
/// and the run takes its stated stand-in and says so (<see cref="SetupRules"/>).</para>
/// </summary>
public static class PlanningSetupCodec
{
    private const int Version = 1;
    private const string TimeFormat = "HH:mm";
    private const int LongestDinnerMinutes = 240;

    public const string NothingSavedReason = "No household has been set up on this phone.";
    public const string ShapeReason = "The saved household setup is not in a shape this version reads.";
    public const string NoAdultReason = "The saved household setup names no adult.";
    public const string UnnamedAdultReason = "An adult in the saved household setup has no name.";
    public const string SharedNameReason = "Two adults in the saved household setup share a name.";

    public static HouseholdSetupRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HouseholdSetupRead.NotSet(NothingSavedReason);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            // Position only: the exception's text can quote the document, and the setup holds addresses.
            var where = failure.LineNumber is { } line && failure.BytePositionInLine is { } column
                ? $" (line {line + 1}, position {column + 1})"
                : string.Empty;
            return new HouseholdSetupRead.NotSet($"The saved household setup is not valid JSON{where}.");
        }

        using (document)
        {
            return FromRoot(document.RootElement);
        }
    }

    public static string Write(PlanningSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            if (setup.ZoneId is { } zone)
            {
                writer.WriteString("zone", zone);
            }

            writer.WriteStartArray("adults");
            foreach (var adult in setup.Adults)
            {
                writer.WriteStartObject();
                writer.WriteString("name", adult.Name);
                if (adult.CalendarId is { } calendar)
                {
                    writer.WriteString("calendar", calendar);
                }

                if (adult.OnPickup is { } pickup)
                {
                    writer.WriteBoolean("pickup", pickup);
                }

                WriteList(writer, "addresses", adult.Addresses);
                WriteList(writer, "otherNames", adult.OtherNames);
                if (adult.DeliversHere)
                {
                    writer.WriteBoolean("deliversHere", true);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (setup.FamilyCalendarId is { } family)
            {
                writer.WriteString("familyCalendar", family);
            }

            if (setup.NoFamilyCalendar)
            {
                writer.WriteBoolean("noFamilyCalendar", true);
            }

            WriteList(writer, "childrenCalendars", setup.ChildrenCalendarIds);
            if (setup.NoChildren)
            {
                writer.WriteBoolean("noChildren", true);
            }

            if (setup.DinnerStart is { } dinner)
            {
                writer.WriteString("dinner", dinner.ToString(TimeFormat, CultureInfo.InvariantCulture));
            }

            if (setup.DinnerLength is { } length)
            {
                writer.WriteNumber("dinnerMinutes", (int)length.TotalMinutes);
            }

            if (setup.Bedtime is { } bedtime)
            {
                writer.WriteString("bedtime", bedtime.ToString(TimeFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static HouseholdSetupRead FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || Find(root, "version") is not { ValueKind: JsonValueKind.Number } version
            || !version.TryGetInt32(out var number)
            || number != Version
            || Find(root, "adults") is not { ValueKind: JsonValueKind.Array } adultList)
        {
            return new HouseholdSetupRead.NotSet(ShapeReason);
        }

        var adults = new List<HouseholdAdult>();
        foreach (var item in adultList.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return new HouseholdSetupRead.NotSet(ShapeReason);
            }

            var name = Text(item, "name");
            if (name is null)
            {
                return new HouseholdSetupRead.NotSet(UnnamedAdultReason);
            }

            if (adults.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return new HouseholdSetupRead.NotSet(SharedNameReason);
            }

            adults.Add(new HouseholdAdult(
                name,
                Text(item, "calendar"),
                Flag(item, "pickup"),
                Texts(item, "addresses"),
                Texts(item, "otherNames"),
                Flag(item, "deliversHere") ?? false));
        }

        if (adults.Count == 0)
        {
            return new HouseholdSetupRead.NotSet(NoAdultReason);
        }

        var length = Find(root, "dinnerMinutes") is { ValueKind: JsonValueKind.Number } minutes
            && minutes.TryGetInt32(out var asked) && asked is > 0 and <= LongestDinnerMinutes
                ? TimeSpan.FromMinutes(asked)
                : (TimeSpan?)null;

        return new HouseholdSetupRead.Configured(new PlanningSetup(adults)
        {
            FamilyCalendarId = Text(root, "familyCalendar"),
            NoFamilyCalendar = Flag(root, "noFamilyCalendar") ?? false,
            ChildrenCalendarIds = Texts(root, "childrenCalendars"),
            NoChildren = Flag(root, "noChildren") ?? false,
            ZoneId = Text(root, "zone"),
            DinnerStart = Clock(root, "dinner"),
            DinnerLength = length,
            Bedtime = Clock(root, "bedtime"),
        });
    }

    private static JsonElement? Find(JsonElement owner, string name)
    {
        foreach (var property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement owner, string name) =>
        Find(owner, name) is { ValueKind: JsonValueKind.String } value && value.GetString()?.Trim() is { Length: > 0 } text ? text : null;

    private static bool? Flag(JsonElement owner, string name) => Find(owner, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    private static List<string> Texts(JsonElement owner, string name) =>
        Find(owner, name) is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(text => text.Length > 0)
                .ToList()
            : [];

    private static TimeOnly? Clock(JsonElement owner, string name) =>
        Text(owner, name) is { } text
        && TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
