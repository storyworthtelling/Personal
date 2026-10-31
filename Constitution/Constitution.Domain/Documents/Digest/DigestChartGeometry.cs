using Constitution.Domain.Body;

namespace Constitution.Domain.Documents;

/// <summary>One signal's line ready to draw: solid runs, dotted bridges across the weeks nothing was read, and the latest week's dot.</summary>
public sealed record DigestChart(IReadOnlyList<IReadOnlyList<ChartPoint>> Runs, IReadOnlyList<ChartBridge> Bridges, ChartPoint? Dot);

/// <summary>
/// The pure geometry of the digest's weekly line (C-2.6.2, D39), in the chart's own box: x from 0 to the width across the weeks, y from the top, a larger value
/// higher. A week with no reading is a break in the line, bridged by a dotted stroke only between two readings, never drawn as a zero and never reaching past
/// the first or last reading. The dot is the latest week's, when it has one.
/// </summary>
public static class DigestChartGeometry
{
    /// <summary>The share of the height kept clear above and below the line so a dot is not clipped.</summary>
    public const double VerticalMargin = 0.1;

    public static DigestChart Compute(IReadOnlyList<double?> weeks, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        var values = weeks.Where(week => week is not null).Select(week => week!.Value).ToList();
        if (values.Count == 0)
        {
            return new DigestChart([], [], null);
        }

        var low = values.Min();
        var high = values.Max();
        var inner = height * (1 - 2 * VerticalMargin);
        double Y(double value) => high - low <= 0 ? height / 2 : height * VerticalMargin + inner * (1 - (value - low) / (high - low));
        double X(int index) => weeks.Count <= 1 ? 0 : width * index / (weeks.Count - 1);

        var runs = new List<IReadOnlyList<ChartPoint>>();
        List<ChartPoint>? current = null;
        for (var index = 0; index < weeks.Count; index++)
        {
            if (weeks[index] is not { } value)
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                current = [];
                runs.Add(current);
            }

            current.Add(new ChartPoint(X(index), Y(value)));
        }

        var bridges = new List<ChartBridge>();
        for (var run = 0; run + 1 < runs.Count; run++)
        {
            bridges.Add(new ChartBridge(runs[run][^1], runs[run + 1][0]));
        }

        ChartPoint? dot = weeks[^1] is { } latest ? new ChartPoint(X(weeks.Count - 1), Y(latest)) : null;
        return new DigestChart(runs, bridges, dot);
    }
}
