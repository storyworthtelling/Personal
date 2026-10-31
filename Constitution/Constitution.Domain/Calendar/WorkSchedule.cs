using System.Globalization;
using System.Text.Json;

namespace Constitution.Domain.Calendar;

/// <summary>
/// One working block: a person, and the clock times it opens and closes, every weekday. A block
/// whose end is earlier than its start runs overnight into the next morning.
/// </summary>
public sealed record WorkShift(string Person, TimeOnly Start, TimeOnly End);

/// <summary>
/// The work schedule Foreword keeps, read from the personal vault's <c>WorkScheduleJson</c> without
/// depending on Foreword. Constitution only ever reads it (Foreword owns it; the vault sync restores
/// it here and never publishes it back).
///
/// <para><b>The shape</b> is the JSON Foreword's calendar screens save, read from
/// <c>WorkScheduleOptions</c> and <c>CalendarMappingCodec</c> in that project:
/// <c>{"Shifts":[{"Person":"…","Start":"HH:mm","End":"HH:mm","Title":"…","Enabled":true}],
/// "OffKeywords":["Off","PTO","Vacation"]}</c>, property names matched without case. A shift is
/// drawn on weekdays only; a shift with <c>Enabled</c> false is kept in the file and not drawn; a
/// day with an all-day entry carrying an off keyword has no block. The same is done here.</para>
///
/// <para><b>Unknown is not empty.</b> Anything that is not recognisably the schedule (nothing saved,
/// garbled JSON, valid JSON of another shape, a shift whose hours cannot be read) is
/// <see cref="Unrecognised"/>, with a plain reason that never quotes the document. Reading such a
/// document as "no shifts" would free every working day without a word, which is the silent failure
/// the whole model exists to prevent; the model assumes work hours instead and says it did.</para>
/// </summary>
public sealed class WorkSchedule
{
    private static readonly string[] Defaults = ["Off", "PTO", "Vacation"];

    private WorkSchedule(bool isKnown, string? problem, IReadOnlyList<WorkShift> shifts, IReadOnlyList<string> offKeywords)
    {
        IsKnown = isKnown;
        Problem = problem;
        Shifts = shifts;
        OffKeywords = offKeywords;
    }

    /// <summary>True when a schedule was found and every enabled shift in it could be read.</summary>
    public bool IsKnown { get; }

    /// <summary>Why the schedule is unknown, as a plain sentence; null when it is known.</summary>
    public string? Problem { get; }

    /// <summary>The enabled shifts. Empty when the schedule is unknown, and when a known one has none.</summary>
    public IReadOnlyList<WorkShift> Shifts { get; }

    /// <summary>Words that, as a whole word in the title of an all-day entry, make the day a day off.</summary>
    public IReadOnlyList<string> OffKeywords { get; }

    /// <summary>Foreword's own default off-day words, used when its file names none.</summary>
    public static IReadOnlyList<string> DefaultOffKeywords => Defaults;

    /// <summary>A known schedule built in code (the tests, or a settings screen).</summary>
    public static WorkSchedule Of(IEnumerable<WorkShift> shifts, IEnumerable<string>? offKeywords = null)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        var kept = shifts.ToList();
        foreach (var shift in kept)
        {
            if (string.IsNullOrWhiteSpace(shift.Person))
            {
                throw new ArgumentException("A work shift needs a person.", nameof(shifts));
            }

            if (shift.Start == shift.End)
            {
                throw new ArgumentException("A work shift cannot start and end at the same time.", nameof(shifts));
            }
        }

        return new WorkSchedule(true, null, kept, CleanWords(offKeywords ?? Defaults));
    }

    /// <summary>A schedule that is not known, with the reason.</summary>
    public static WorkSchedule Unrecognised(string problem)
    {
        if (string.IsNullOrWhiteSpace(problem))
        {
            throw new ArgumentException("An unrecognised work schedule has to say why.", nameof(problem));
        }

        return new WorkSchedule(false, problem.Trim(), [], []);
    }

    /// <summary>
    /// Reads the JSON. Never throws on a bad document: it returns an <see cref="Unrecognised"/> schedule
    /// carrying the reason, so the caller can fall back to assumed hours and say so.
    /// </summary>
    public static WorkSchedule Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Unrecognised("No work schedule has been saved on this account.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            // Position only: the exception's text can quote the document, and the schedule is personal.
            var where = failure.LineNumber is { } line && failure.BytePositionInLine is { } column
                ? $" (line {line + 1}, position {column + 1})"
                : string.Empty;
            return Unrecognised($"The saved work schedule is not valid JSON{where}.");
        }

        using (document)
        {
            return FromRoot(document.RootElement);
        }
    }

    /// <summary>The enabled shifts for one person, matched without case. A split shift is several.</summary>
    public IReadOnlyList<WorkShift> ShiftsFor(string person) =>
        Shifts.Where(shift => string.Equals(shift.Person.Trim(), person?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private static WorkSchedule FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Unrecognised("The saved work schedule is not a JSON object.");
        }

        if (!TryProperty(root, "Shifts", out var list))
        {
            return Unrecognised("The saved work schedule has no Shifts list, so it is not the schedule Foreword keeps.");
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            return Unrecognised("Shifts in the saved work schedule is not a list.");
        }

        var shifts = new List<WorkShift>();
        var number = 0;
        foreach (var element in list.EnumerateArray())
        {
            number++;
            var read = ReadShift(element, number, out var problem);
            if (problem is not null)
            {
                return Unrecognised(problem);
            }

            if (read is not null)
            {
                shifts.Add(read);
            }
        }

        IReadOnlyList<string> words = Defaults;
        if (TryProperty(root, "OffKeywords", out var keywords))
        {
            if (keywords.ValueKind != JsonValueKind.Array || keywords.EnumerateArray().Any(word => word.ValueKind != JsonValueKind.String))
            {
                return Unrecognised("OffKeywords in the saved work schedule is not a list of words.");
            }

            words = CleanWords(keywords.EnumerateArray().Select(word => word.GetString()!));
        }

        return new WorkSchedule(true, null, shifts, words);
    }

    /// <summary>One shift, or null for a shift switched off. A problem comes back instead of throwing.</summary>
    private static WorkShift? ReadShift(JsonElement element, int number, out string? problem)
    {
        problem = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            problem = $"Shift {number} in the Shifts list of the saved work schedule is not an object.";
            return null;
        }

        if (TryProperty(element, "Enabled", out var enabled))
        {
            if (enabled.ValueKind == JsonValueKind.False)
            {
                // Kept in Foreword's file so the hours need not be typed again; drawn nowhere.
                return null;
            }

            if (enabled.ValueKind != JsonValueKind.True)
            {
                problem = $"Shift {number} has an Enabled value that is neither true nor false.";
                return null;
            }
        }

        if (!TryProperty(element, "Person", out var person) || person.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(person.GetString()))
        {
            problem = $"Shift {number} has no readable Person.";
            return null;
        }

        if (!TryTime(element, "Start", out var start))
        {
            problem = $"Shift {number} has no readable Start time (expected HH:mm).";
            return null;
        }

        if (!TryTime(element, "End", out var end))
        {
            problem = $"Shift {number} has no readable End time (expected HH:mm).";
            return null;
        }

        if (start == end)
        {
            problem = $"Shift {number} starts and ends at the same time.";
            return null;
        }

        return new WorkShift(person.GetString()!.Trim(), start, end);
    }

    private static bool TryTime(JsonElement shift, string name, out TimeOnly time)
    {
        time = default;
        return TryProperty(shift, name, out var value)
            && value.ValueKind == JsonValueKind.String
            && TimeOnly.TryParseExact(value.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    // Foreword reads its own file without regard to case; the first match wins.
    private static bool TryProperty(JsonElement owner, string name, out JsonElement value)
    {
        foreach (var property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static List<string> CleanWords(IEnumerable<string> words) =>
        words.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList();
}
