namespace Constitution.Domain.Body;

/// <summary>
/// Turns a signal's platform samples into one <see cref="BodyReading"/> a day.
///
/// <para>Two shapes of signal. <b>Spot measures</b> (weight, body fat, resting heart rate, heart-rate
/// variability) are taken several times a day, and the day is their average. <b>Running totals</b> (steps,
/// active minutes, sleep) are added up within one source, but never across sources: a phone and a watch
/// that both counted the same walk would otherwise report double, and Health Connect leaves that
/// de-duplication to the reader. The day takes the fuller source's count.</para>
///
/// <para>An amount that is zero, negative or not a number is not a reading: a watch left on the dresser
/// reads zero steps, and a chart that drew it would be saying something false. It is a gap.</para>
/// </summary>
public static class DailyReadings
{
    /// <summary>One reading a day for the days that have a usable sample, oldest first.</summary>
    /// <exception cref="ArgumentException"><paramref name="signal"/> is not one Health Connect supplies.</exception>
    public static IReadOnlyList<BodyReading> From(BodySignal signal, IEnumerable<HealthSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!BodySignals.HealthConnect.Contains(signal))
        {
            throw new ArgumentException($"{signal} is worked out from the pages and meals, not read from Health Connect.", nameof(signal));
        }

        var usable = samples.Where(sample => double.IsFinite(sample.Amount) && sample.Amount > 0).ToList();
        var byDay = usable.GroupBy(sample => sample.Day).OrderBy(group => group.Key);

        return byDay
            .Select(group => new BodyReading(signal, group.Key, IsRunningTotal(signal) ? FullestSource(group) : group.Average(sample => sample.Amount)))
            .ToList();
    }

    private static bool IsRunningTotal(BodySignal signal) =>
        signal is BodySignal.Steps or BodySignal.ActiveMinutes or BodySignal.Sleep;

    private static double FullestSource(IEnumerable<HealthSample> day) =>
        day.GroupBy(sample => sample.Origin, StringComparer.Ordinal).Max(source => source.Sum(sample => sample.Amount));
}
