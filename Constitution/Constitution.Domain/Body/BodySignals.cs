using Constitution.Domain.Voice;

namespace Constitution.Domain.Body;

/// <summary>What is fixed about each <see cref="BodySignal"/>: its stored id, its name, its units and which way rising points.</summary>
public static class BodySignals
{
    private sealed record Spec(string Id, string Name, string Units, RisingIs Rising, double SteadyBand);

    // The ids are literals on purpose (frozen, see BodySignalTests). The steady bands are the voice
    // seats' own (SeatWords.SteadyBand) wherever a seat has one, so the monthly sentence and the tab
    // can never disagree about whether a signal moved; strength and calories have no seat unit.
    private static Spec Of(BodySignal signal) => signal switch
    {
        BodySignal.Weight => new Spec("weight", "Weight", "lb", RisingIs.Worse, SeatWords.SteadyBand(SignalUnit.Pounds)),
        BodySignal.BodyComposition => new Spec("body-composition", "Body fat", "percent", RisingIs.Worse, SeatWords.SteadyBand(SignalUnit.Percent)),
        BodySignal.RestingHeartRate => new Spec("resting-heart-rate", "Resting heart rate", "bpm", RisingIs.Worse, SeatWords.SteadyBand(SignalUnit.BeatsPerMinute)),
        BodySignal.HeartRateVariability => new Spec("heart-rate-variability", "Heart rate variability", "ms", RisingIs.Better, SeatWords.SteadyBand(SignalUnit.Milliseconds)),
        BodySignal.Steps => new Spec("steps", "Steps", "a day", RisingIs.Better, SeatWords.SteadyBand(SignalUnit.Steps)),
        BodySignal.ActiveMinutes => new Spec("active-minutes", "Active minutes", "minutes", RisingIs.Better, SeatWords.SteadyBand(SignalUnit.Minutes)),
        BodySignal.Sleep => new Spec("sleep", "Sleep", "hours", RisingIs.Better, SeatWords.SteadyBand(SignalUnit.Hours)),
        BodySignal.Strength => new Spec("strength", "Strength", "lb", RisingIs.Better, 10),
        BodySignal.Calories => new Spec("calories", "Calories", "kcal", RisingIs.Neutral, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "That is not one of Body's signals."),
    };

    /// <summary>The signals Health Connect supplies, in the order the plan names them.</summary>
    public static IReadOnlyList<BodySignal> HealthConnect { get; } =
    [
        BodySignal.Weight, BodySignal.BodyComposition, BodySignal.RestingHeartRate, BodySignal.HeartRateVariability,
        BodySignal.Steps, BodySignal.ActiveMinutes, BodySignal.Sleep,
    ];

    /// <summary>The signals worked out from the pages and meals rather than read from the platform.</summary>
    public static IReadOnlyList<BodySignal> Derived { get; } = [BodySignal.Strength, BodySignal.Calories];

    /// <summary>All nine: Health Connect's, then the derived ones.</summary>
    public static IReadOnlyList<BodySignal> All { get; } = [.. HealthConnect, .. Derived];

    /// <summary>The signal's frozen wire id, as stored in the Body backup.</summary>
    public static string Id(BodySignal signal) => Of(signal).Id;

    /// <summary>Reads a stored id. False for one this version does not know, so a newer backup can be skipped past, not crashed on.</summary>
    public static bool TryFromId(string? id, out BodySignal signal)
    {
        foreach (var candidate in All)
        {
            if (Of(candidate).Id == id)
            {
                signal = candidate;
                return true;
            }
        }

        signal = default;
        return false;
    }

    /// <summary>Which way rising points for this signal (D70: a rising weight is worse).</summary>
    public static RisingIs Rising(BodySignal signal) => Of(signal).Rising;

    /// <summary>The name on a chart's form head and in the signal's page title.</summary>
    public static string Name(BodySignal signal) => Of(signal).Name;

    /// <summary>The units word on a chart's form head: "hours", "lb", "bpm", "ms", "a day".</summary>
    public static string Units(BodySignal signal) => Of(signal).Units;

    /// <summary>A move smaller than this is a wobble, not a move.</summary>
    internal static double SteadyBand(BodySignal signal) => Of(signal).SteadyBand;
}
