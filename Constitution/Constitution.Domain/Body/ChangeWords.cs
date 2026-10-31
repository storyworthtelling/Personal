using System.Globalization;

namespace Constitution.Domain.Body;

/// <summary>
/// A <see cref="SignalChange"/> said in words, in the signal's own unit: "down about 40 minutes a
/// night", "down 2.2 lb", "up about 1,500 steps a day". Never a percentage and never against anyone
/// but the owner. "About" appears only where the number was rounded to be sayable. Invariant culture
/// throughout, so a device's language cannot turn "1,500" into "1.500".
/// </summary>
public static class ChangeWords
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"about the same" inside a wobble, otherwise direction, amount and unit.</summary>
    public static string Phrase(SignalChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.IsSteady)
        {
            return "about the same";
        }

        var direction = change.Change > 0 ? "up" : "down";
        var gap = Math.Abs(change.Change);
        return $"{direction} {Amount(change.Signal, gap)}";
    }

    private static string Amount(BodySignal signal, double gap) => signal switch
    {
        BodySignal.Sleep => SleepAmount(gap),
        BodySignal.Weight => $"{gap.ToString("0.#", Invariant)} lb",
        BodySignal.BodyComposition => $"{gap.ToString("0.#", Invariant)} points",
        BodySignal.RestingHeartRate => $"{gap.ToString("0", Invariant)} bpm",
        BodySignal.HeartRateVariability => $"{gap.ToString("0", Invariant)} ms",
        BodySignal.Steps => $"about {Rounded(gap, 100).ToString("N0", Invariant)} steps a day",
        BodySignal.ActiveMinutes => $"{gap.ToString("0", Invariant)} minutes a day",
        BodySignal.Strength => $"{gap.ToString("0", Invariant)} lb",
        BodySignal.Calories => $"about {Rounded(gap, 10).ToString("N0", Invariant)} kcal a day",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "That is not one of Body's signals."),
    };

    /// <summary>Sleep is held in hours but said in minutes, to the nearest five, until it is two hours or more.</summary>
    private static string SleepAmount(double hours)
    {
        var minutes = Rounded(hours * 60, 5);
        return minutes >= 120
            ? $"about {hours.ToString("0.#", Invariant)} hours a night"
            : $"about {minutes.ToString("0", Invariant)} minutes a night";
    }

    private static double Rounded(double value, double step) => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
}
