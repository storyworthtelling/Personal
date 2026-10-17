using System.Globalization;

namespace Constitution.Domain.Voice;

/// <summary>
/// The words and the checks the facts builders and the fallbacks share, so a number is said one way
/// everywhere and a bad input is refused one way everywhere. Pure; invariant culture throughout (a
/// device's language must not change "1,200" into "1.200" or "Nov" into "nov." in a stored line).
/// </summary>
internal static class SeatWords
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly string[] CountWords =
        ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve"];

    // ---- sentences ----

    public static string Sentence(string text) =>
        text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));

    public static string CountWord(int count) =>
        count >= 0 && count < CountWords.Length ? CountWords[count] : count.ToString(Invariant);

    /// <summary>"5:30" on the twelve-hour clock (DESIGN.md §5); the meridiem is given separately.</summary>
    public static string Clock(TimeOnly time) =>
        $"{(time.Hour % 12 == 0 ? 12 : time.Hour % 12).ToString(Invariant)}:{time.Minute.ToString("00", Invariant)}";

    public static string Meridiem(TimeOnly time) => time.Hour < 12 ? "AM" : "PM";

    /// <summary>Days in the order of the week, Monday first, each once: "Tuesday, Thursday and Saturday".</summary>
    public static string Days(IEnumerable<DayOfWeek> days)
    {
        var names = days.Distinct().OrderBy(day => ((int)day + 6) % 7)
            .Select(day => day.ToString()).ToList();
        return names.Count <= 1
            ? string.Concat(names)
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    public static string ShortDate(DateOnly date) => date.ToString("MMM d", Invariant);

    // ---- dinner ----

    public static string DinnerSentence(DinnerPlan dinner) =>
        dinner.IsOut ? "Dinner is out tonight." : Sentence(dinner.Dish!) + " tonight.";

    public static string DinnerFact(DinnerPlan dinner) =>
        dinner.IsOut ? "Dinner tonight is out; nobody is cooking at home." : $"Dinner tonight: {dinner.Dish}.";

    // ---- signals ----

    /// <summary>A reading as it is said: "6.2 hours", "184 lb", "9,200 steps".</summary>
    public static string Reading(SignalUnit unit, double value)
    {
        var (singular, plural, format) = Spec(unit);
        var text = value.ToString(format, Invariant);
        return $"{text} {(text == "1" ? singular : plural)}";
    }

    /// <summary>
    /// How far apart two readings are, said in the unit people think in (sleep in minutes). Null when
    /// the gap rounds to nothing, which is how "about the same" is told from a move.
    /// </summary>
    public static string? Gap(SignalUnit unit, double first, double second)
    {
        var gap = Math.Abs(first - second);
        if (unit == SignalUnit.Hours)
        {
            var minutes = Math.Round(gap * 60, MidpointRounding.AwayFromZero);
            if (minutes < 1)
            {
                return null;
            }

            return minutes >= 120
                ? Reading(SignalUnit.Hours, gap)
                : Reading(SignalUnit.Minutes, minutes);
        }

        var (_, _, format) = Spec(unit);
        var shown = gap.ToString(format, Invariant);
        return double.Parse(shown, Invariant) == 0 ? null : Reading(unit, gap);
    }

    /// <summary>A month that differs from the last by less than this is "close to last month", not a move.</summary>
    public static double SteadyBand(SignalUnit unit) => unit switch
    {
        SignalUnit.Hours => 0.25,
        SignalUnit.Pounds => 1,
        SignalUnit.BeatsPerMinute => 2,
        SignalUnit.Milliseconds => 5,
        SignalUnit.Steps => 500,
        SignalUnit.Minutes => 5,
        SignalUnit.Percent => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "This unit is not one a signal is measured in."),
    };

    private static (string Singular, string Plural, string Format) Spec(SignalUnit unit) => unit switch
    {
        SignalUnit.Hours => ("hour", "hours", "0.#"),
        SignalUnit.Pounds => ("lb", "lb", "0.#"),
        SignalUnit.BeatsPerMinute => ("beat a minute", "beats a minute", "0"),
        SignalUnit.Milliseconds => ("ms", "ms", "0"),
        SignalUnit.Steps => ("step", "steps", "N0"),
        SignalUnit.Minutes => ("minute", "minutes", "0"),
        SignalUnit.Percent => ("percent", "percent", "0.#"),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "This unit is not one a signal is measured in."),
    };

    // ---- a plain number with a free-text unit (loads) ----

    public static string Number(double value) => value.ToString("0.#", Invariant);

    // ---- checks ----

    public static string Named(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException($"A seat's facts need {what}; it was blank.", nameof(text));
        }

        return text.Trim();
    }

    public static double Finite(double value, string what)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException($"A seat's facts need a real number for {what}.", nameof(value));
        }

        return value;
    }

    public static void Known(SignalUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "This unit is not one a signal is measured in.");
        }
    }

    public static IEnumerable<DayOfWeek> KnownDays(IReadOnlyList<DayOfWeek>? days, string what)
    {
        ArgumentNullException.ThrowIfNull(days, what);
        foreach (var day in days)
        {
            if (!Enum.IsDefined(day))
            {
                throw new ArgumentOutOfRangeException(what, day, "That is not a day of the week.");
            }
        }

        return days;
    }
}
