using System.Globalization;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Body.Screens;

/// <summary>
/// The plain words a chart and a signal's page are lettered in: the latest value in the signal's own
/// shorthand, when it was read, and what the owner's own past is called. Invariant culture throughout,
/// so a device's language cannot turn "7,400" into "7.400".
/// </summary>
internal static class SignalWording
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The latest value as it is printed in mono: "6.2 h", "179.2 lb", "59 bpm", "7,400".</summary>
    public static string ValueText(BodySignal signal, double value) => signal switch
    {
        BodySignal.Sleep => $"{value.ToString("0.#", Invariant)} h",
        BodySignal.Weight => $"{value.ToString("0.#", Invariant)} lb",
        BodySignal.BodyComposition => $"{value.ToString("0.#", Invariant)}%",
        BodySignal.RestingHeartRate => $"{value.ToString("0", Invariant)} bpm",
        BodySignal.HeartRateVariability => $"{value.ToString("0", Invariant)} ms",

        // The form head already says "A DAY", so the figure stands alone, as the book draws it.
        BodySignal.Steps => value.ToString("N0", Invariant),
        BodySignal.ActiveMinutes => $"{value.ToString("0", Invariant)} min",
        BodySignal.Strength => $"{value.ToString("0", Invariant)} lb",
        BodySignal.Calories => $"{value.ToString("N0", Invariant)} kcal",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "That is not one of Body's signals."),
    };

    /// <summary>
    /// When the latest reading was taken, in the words a person would use. Sleep is dated by the morning it
    /// ended, so the same reading is "last night" on its own morning; a weigh-in is "this morning".
    /// </summary>
    public static string WhenText(BodySignal signal, DateOnly day, DateOnly today)
    {
        var back = Math.Max(0, today.DayNumber - day.DayNumber);
        if (signal == BodySignal.Sleep)
        {
            var night = day.AddDays(-1);
            return back switch
            {
                0 => "last night",
                1 => "the night before last",
                < 7 => $"{night.DayOfWeek} night",
                _ => $"the night of {night.ToString("MMM d", Invariant)}",
            };
        }

        return back switch
        {
            0 => signal == BodySignal.Weight ? "this morning" : "today",
            1 => "yesterday",
            < 7 => day.DayOfWeek.ToString(),
            _ => $"on {day.ToString("MMM d", Invariant)}",
        };
    }

    /// <summary>The date line of a signal's page: "Tuesday, Nov 17".</summary>
    public static string DateText(DateOnly day) => day.ToString("dddd, MMM d", Invariant);

    /// <summary>
    /// What the owner's own past is called when the seat sets today against it: the calendar month most of the
    /// previous stretch fell in ("October"), or the quarter or year before.
    /// </summary>
    public static string PastLabel(BodyWindow window, DateOnly today)
    {
        switch (window)
        {
            case BodyWindow.Quarter:
                return "last quarter";
            case BodyWindow.Year:
                return "last year";
        }

        var days = BodyWindows.Days(window);
        var month = Enumerable.Range(days, days)
            .Select(back => today.AddDays(-back))
            .GroupBy(day => (day.Year, day.Month))
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .First().Key;
        return new DateTime(month.Year, month.Month, 1).ToString("MMMM", Invariant);
    }

    /// <summary>The unit a voice seat measures the signal in, or null where a seat has none (calories).</summary>
    public static SignalUnit? SeatUnit(BodySignal signal) => signal switch
    {
        BodySignal.Sleep => SignalUnit.Hours,
        BodySignal.Weight => SignalUnit.Pounds,
        BodySignal.Strength => SignalUnit.Pounds,
        BodySignal.BodyComposition => SignalUnit.Percent,
        BodySignal.RestingHeartRate => SignalUnit.BeatsPerMinute,
        BodySignal.HeartRateVariability => SignalUnit.Milliseconds,
        BodySignal.Steps => SignalUnit.Steps,
        BodySignal.ActiveMinutes => SignalUnit.Minutes,
        _ => null,
    };

    /// <summary>The weekday a night belongs to: the evening before the morning the reading is dated.</summary>
    public static DayOfWeek NightOf(DateOnly datedMorning) => datedMorning.AddDays(-1).DayOfWeek;
}
