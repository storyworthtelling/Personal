using Constitution.Domain.Body;

namespace Constitution.Domain.Workouts;

/// <summary>What the morning's signals add up to.</summary>
public enum RecoveryState
{
    /// <summary>Too little to judge (a fresh install, no baseline, nothing recent): the plan is left exactly as it is.</summary>
    NotEnoughToSay,

    Recovered,

    /// <summary>At least one signal is off against the owner's own past: a hard session becomes mobility.</summary>
    UnderRecovered,

    /// <summary>Two or more signals are well off at once. Swaps like <see cref="UnderRecovered"/>, and is one of the two things that earn the doctor suggestion.</summary>
    Alarming,
}

/// <summary>Why a morning read as under-recovered: a closed list, so the reason line can name a signal without carrying a number. In the order the line names them.</summary>
public enum RecoveryReason
{
    LowHeartRateVariability,

    ShortSleep,

    RaisedRestingHeartRate,
}

/// <summary>What the read found, and what it had to make do with. It holds words and signal names, never a reading (D98).</summary>
/// <param name="StaleSignals">Signals judged from an earlier day's value because the morning's had not been read (R5). The caller notes the gap in Diagnostics (a count, never the values).</param>
/// <param name="MissingSignals">Signals that could not be judged: nothing recent enough to use, or too little of the owner's own past to compare it with.</param>
public sealed record RecoveryRead(
    RecoveryState State,
    IReadOnlyList<RecoveryReason> Reasons,
    IReadOnlyList<BodySignal> StaleSignals,
    IReadOnlyList<BodySignal> MissingSignals)
{
    public bool UsedStaleValues => StaleSignals.Count > 0;

    public static RecoveryRead Nothing => new(RecoveryState.NotEnoughToSay, [], [], []);
}

/// <summary>
/// Reads the morning's recovery from Body's stored readings (C-5.2.4): resting heart rate, heart-rate variability and
/// sleep, each against the owner's <i>own</i> average over the <see cref="BaselineDays"/> before it, never a norm.
///
/// <para><b>The rules</b> (every number is a plan decision with no source in the book, named here so it is one edit and
/// one test; none has met a real body). Sleep is short when it is <see cref="ShortSleepHours"/> or more below the
/// owner's average; HRV is low at <see cref="LowHrvRatio"/> of it or less; resting heart rate is raised at
/// <see cref="RaisedRestingHeartRateBeats"/> or more above it. Any one signal is under-recovery. A signal is <i>far</i>
/// off at <see cref="FarShortSleepHours"/> / <see cref="FarLowHrvRatio"/> / <see cref="FarRaisedRestingHeartRateBeats"/>,
/// and two far-off signals together are <see cref="RecoveryState.Alarming"/>.</para>
///
/// <para><b>No background read (R5).</b> The morning's value is the one dated the morning (sleep belongs to the day it
/// ended). When there is none, the newest value from the <see cref="StaleDays"/> before is used, judged against the days
/// before <i>it</i>, and the signal is named in <see cref="RecoveryRead.StaleSignals"/>. Nothing older is used. A signal
/// with fewer than a week of its own past to compare with is not judged. Nothing judgeable is
/// <see cref="RecoveryState.NotEnoughToSay"/>, and the plan is left alone.</para>
///
/// <para><b>Device-flagged.</b> Whether Health Connect delivers any of this to the phone by morning, and what an HRV from
/// the owner's watch means in these units, is exactly what no test here can say.</para>
/// </summary>
public static class RecoveryReader
{
    /// <summary>How many days back a value read earlier is still used when the morning's was never read.</summary>
    public const int StaleDays = 3;

    /// <summary>How many days of the owner's own past a signal is compared with.</summary>
    public const int BaselineDays = 28;

    /// <summary>A night this many hours shorter than the owner's own average is short.</summary>
    public const double ShortSleepHours = 1.0;

    /// <summary>A night this many hours shorter is far off.</summary>
    public const double FarShortSleepHours = 2.0;

    /// <summary>An HRV at this share of the owner's own average (or less) is low.</summary>
    public const double LowHrvRatio = 0.8;

    /// <summary>An HRV at this share (or less) is far off.</summary>
    public const double FarLowHrvRatio = 0.6;

    /// <summary>A resting heart rate this many beats a minute above the owner's own average is raised.</summary>
    public const double RaisedRestingHeartRateBeats = 5;

    /// <summary>A resting heart rate this many beats above is far off.</summary>
    public const double FarRaisedRestingHeartRateBeats = 10;

    private static readonly BodySignal[] Signals = [BodySignal.RestingHeartRate, BodySignal.HeartRateVariability, BodySignal.Sleep];

    public static RecoveryRead Read(IEnumerable<BodyReading> readings, DateOnly morning)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var usable = readings
            .Where(reading => Signals.Contains(reading.Signal) && double.IsFinite(reading.Value) && reading.Value > 0 && reading.Day <= morning)
            .ToList();

        var stale = new List<BodySignal>();
        var missing = new List<BodySignal>();
        var reasons = new List<RecoveryReason>();
        var far = 0;
        var judged = 0;

        foreach (var signal in Signals)
        {
            // A later reading of the same day replaces an earlier one.
            var byDay = new Dictionary<DateOnly, double>();
            foreach (var reading in usable.Where(item => item.Signal == signal))
            {
                byDay[reading.Day] = reading.Value;
            }

            var day = CurrentDay(byDay, morning);
            if (day is not { } current)
            {
                missing.Add(signal);
                continue;
            }

            var past = byDay.Where(pair => pair.Key < current && pair.Key >= current.AddDays(-BaselineDays)).Select(pair => pair.Value).ToList();
            if (past.Count < SignalChange.EnoughDaysPerStretch)
            {
                missing.Add(signal);
                continue;
            }

            judged++;
            if (current != morning)
            {
                stale.Add(signal);
            }

            var (off, isFar) = Judge(signal, byDay[current], past.Average());
            if (off)
            {
                reasons.Add(ReasonOf(signal));
                far += isFar ? 1 : 0;
            }
        }

        var state = judged == 0
            ? RecoveryState.NotEnoughToSay
            : reasons.Count == 0 ? RecoveryState.Recovered : far >= 2 ? RecoveryState.Alarming : RecoveryState.UnderRecovered;
        return new RecoveryRead(state, reasons.OrderBy(reason => reason).ToList(), stale, missing);
    }

    private static DateOnly? CurrentDay(Dictionary<DateOnly, double> byDay, DateOnly morning)
    {
        if (byDay.ContainsKey(morning))
        {
            return morning;
        }

        var earliest = morning.AddDays(-StaleDays);
        var newest = byDay.Keys.Where(day => day < morning && day >= earliest).DefaultIfEmpty().Max();
        return newest == default ? null : newest;
    }

    private static (bool Off, bool Far) Judge(BodySignal signal, double value, double average) => signal switch
    {
        BodySignal.Sleep => (value - average <= -ShortSleepHours, value - average <= -FarShortSleepHours),
        BodySignal.HeartRateVariability => (value <= average * LowHrvRatio, value <= average * FarLowHrvRatio),
        _ => (value - average >= RaisedRestingHeartRateBeats, value - average >= FarRaisedRestingHeartRateBeats),
    };

    private static RecoveryReason ReasonOf(BodySignal signal) => signal switch
    {
        BodySignal.Sleep => RecoveryReason.ShortSleep,
        BodySignal.HeartRateVariability => RecoveryReason.LowHeartRateVariability,
        _ => RecoveryReason.RaisedRestingHeartRate,
    };
}
