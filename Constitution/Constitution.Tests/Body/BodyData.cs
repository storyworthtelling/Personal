using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>Invented readings for the Body tests: a made-up week of made-up numbers, nobody's real ones.</summary>
internal static class BodyData
{
    public static readonly DateOnly Today = new(2026, 11, 17);

    /// <summary>The first day the Month window shows when <see cref="Today"/> is the last.</summary>
    public static DateOnly MonthStart => Today.AddDays(-29);

    public static BodyReading Reading(BodySignal signal, DateOnly day, double value) => new(signal, day, value);

    /// <summary>One reading a day for <paramref name="days"/> days ending <paramref name="endingOn"/>.</summary>
    public static IEnumerable<BodyReading> Daily(BodySignal signal, DateOnly endingOn, int days, double value) =>
        Enumerable.Range(0, days).Select(back => Reading(signal, endingOn.AddDays(-back), value));

    /// <summary>The current month at one value and the month before at another.</summary>
    public static List<BodyReading> TwoMonths(BodySignal signal, double previous, double current)
    {
        var readings = Daily(signal, Today, 30, current).ToList();
        readings.AddRange(Daily(signal, Today.AddDays(-30), 30, previous));
        return readings;
    }

    public static SignalChange Change(BodySignal signal, double current, double previous) =>
        new(signal, current, previous);

    /// <summary>The points of every drawn line, flattened, for comparing two geometries.</summary>
    public static List<(double X, double Y)> Flatten(ChartGeometry geometry) =>
        geometry.Lines.SelectMany(line => line).Select(point => (point.X, point.Y)).ToList();
}
