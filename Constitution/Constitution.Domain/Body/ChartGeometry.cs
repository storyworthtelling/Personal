namespace Constitution.Domain.Body;

/// <summary>A place on a chart, in the width and height the chart was computed for: x grows rightwards, y grows downwards.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>The dotted bridge drawn across one or more missing days, from the last reading before them to the first after.</summary>
public readonly record struct ChartBridge(ChartPoint From, ChartPoint To);

/// <summary>
/// Where a signal's one neutral line goes. This is places and readings only: no colour, no role, no
/// verdict, and nothing it could be told one from (<see cref="Compute"/> takes readings, a window, a day
/// and a size, so a rising weight and a rising sleep draw the same). The painter draws
/// <see cref="Lines"/> solid in the neutral ink, <see cref="Bridges"/> faintly dotted, and a dot at
/// <see cref="Dot"/>. There is no overlay, because there is nothing but the owner's own readings to overlay.
/// </summary>
/// <param name="Lines">Runs of consecutive days with at least two readings, left to right. A day with no reading ends a run.</param>
/// <param name="Bridges">One per stretch of missing days between two readings, in order.</param>
/// <param name="Dot">The latest reading's place, or null when the window holds none.</param>
/// <param name="LatestDay">The day of the latest reading in the window.</param>
/// <param name="LatestValue">That reading's value, in the signal's canonical unit.</param>
public sealed record ChartGeometry(
    IReadOnlyList<IReadOnlyList<ChartPoint>> Lines,
    IReadOnlyList<ChartBridge> Bridges,
    ChartPoint? Dot,
    DateOnly? LatestDay,
    double? LatestValue)
{
    /// <summary>
    /// Fraction of the height left empty above the highest and below the lowest reading, so the dot and
    /// the line are not drawn on the frame.
    /// </summary>
    public const double VerticalMargin = 0.1;

    /// <summary>
    /// Lays one signal's readings out over a window of <see cref="BodyWindows.Days"/> days ending
    /// <paramref name="today"/>, across <paramref name="width"/> by <paramref name="height"/>. The first day sits
    /// at x = 0 and today at x = width, so a line that stops short of the right edge shows days with no
    /// reading (the bed offline) as empty space, never as a drawn line. The vertical scale is the lowest to
    /// the highest reading <em>in the window</em>, so the shape is the owner's own recent range and the form
    /// head carries the numbers; a constant series sits mid-height. Readings outside the window are
    /// ignored; two readings of one day keep the later one in the sequence given; the order of different
    /// days does not matter.
    /// </summary>
    /// <exception cref="ArgumentNullException">No readings sequence.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The window is not one of Body's, or the size is not a positive real number.</exception>
    /// <exception cref="ArgumentException">The readings mix signals, or one value is not a real number.</exception>
    public static ChartGeometry Compute(IEnumerable<BodyReading> readings, BodyWindow window, DateOnly today, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var days = BodyWindows.Days(window);
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A chart needs a width greater than zero.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A chart needs a height greater than zero.");
        }

        var given = readings.ToList();
        var signals = given.Select(reading => reading.Signal).Distinct().ToList();
        if (signals.Count > 1)
        {
            throw new ArgumentException(
                "One chart draws one signal; these readings are " + string.Join(" and ", signals.Select(BodySignals.Name)) + ".",
                nameof(readings));
        }

        var bad = given.FirstOrDefault(reading => !double.IsFinite(reading.Value));
        if (bad is not null)
        {
            throw new ArgumentException($"A {BodySignals.Name(bad.Signal)} reading on {bad.Day:yyyy-MM-dd} is not a real number.", nameof(readings));
        }

        var first = today.AddDays(1 - days);
        var byDay = new Dictionary<DateOnly, double>();
        foreach (var reading in given.Where(reading => reading.Day >= first && reading.Day <= today))
        {
            byDay[reading.Day] = reading.Value;
        }

        if (byDay.Count == 0)
        {
            return new ChartGeometry([], [], null, null, null);
        }

        var ordered = byDay.OrderBy(pair => pair.Key).ToList();
        var low = ordered.Min(pair => pair.Value);
        var high = ordered.Max(pair => pair.Value);
        var margin = VerticalMargin * height;

        ChartPoint Place(DateOnly day, double value)
        {
            var x = (day.DayNumber - first.DayNumber) / (double)(days - 1) * width;
            var y = high == low
                ? height / 2
                : height - margin - ((value - low) / (high - low) * (height - (2 * margin)));
            return new ChartPoint(x, y);
        }

        var lines = new List<IReadOnlyList<ChartPoint>>();
        var bridges = new List<ChartBridge>();
        var run = new List<ChartPoint>();
        DateOnly? previousDay = null;
        ChartPoint previousPoint = default;
        foreach (var (day, value) in ordered)
        {
            var point = Place(day, value);
            if (previousDay is not null && day.DayNumber - previousDay.Value.DayNumber > 1)
            {
                CloseRun();
                bridges.Add(new ChartBridge(previousPoint, point));
            }

            run.Add(point);
            previousDay = day;
            previousPoint = point;
        }

        CloseRun();
        var latest = ordered[^1];
        return new ChartGeometry(lines, bridges, previousPoint, latest.Key, latest.Value);

        void CloseRun()
        {
            if (run.Count >= 2)
            {
                lines.Add(run.ToList());
            }

            run.Clear();
        }
    }
}
