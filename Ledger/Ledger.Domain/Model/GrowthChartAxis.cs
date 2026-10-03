using System.Globalization;

namespace Index.Domain.Model;

/// <summary>A label on the Growth chart's time axis: the month it sits under and its text.</summary>
public readonly record struct GrowthAxisTick(DateOnly Month, string Label);

/// <summary>
/// The Savings Growth chart's axes (Phase 4, "Charts"; mockup 9, frame 2) as pure arithmetic and pure text.
/// The y axis is a few round money steps below the top of the scale, drawn as dashed hairlines and labelled
/// in a gutter beside the plot ("5k / 10k / 15k" under a $17,500 goal). The x axis is labelled once a year
/// from the first recorded month ("Sep '25 / Sep '26 / Sep '27"), where it used to carry only its two ends.
/// </summary>
public static class GrowthChartAxis
{
    /// <summary>Round steps strictly below <paramref name="max"/> (the top of the scale is the goal line or the
    /// highest balance, which the chart labels itself). Empty when the scale is too small for any.</summary>
    public static IReadOnlyList<decimal> MoneySteps(decimal max) =>
        ChartAxis.NiceSteps(max).Where(step => step < max).ToList();

    /// <summary>The gap, in points, between the "Now" dot and the start of its label.</summary>
    public const int NowLabelGap = 6;

    /// <summary>The first recorded month, then each of its anniversaries up to and including
    /// <paramref name="end"/>. A range under a year has only its start. Labelled with
    /// <see cref="ChartAxis.MonthYearLabel"/> — the one format, with the apostrophe, the Snowball axis uses.
    /// This is every year; a long horizon wants <see cref="YearTicks(DateOnly, DateOnly, float, float)"/>.</summary>
    public static IReadOnlyList<GrowthAxisTick> YearTicks(DateOnly first, DateOnly end) => TicksEvery(first, end, 1);

    /// <summary>
    /// <see cref="YearTicks(DateOnly, DateOnly)"/> thinned to what the plot has room to say: every
    /// <see cref="YearStride"/>th anniversary, so no two labels sit closer than
    /// <paramref name="minSpacing"/> points across a plot <paramref name="plotWidth"/> wide. A goal the pace
    /// reaches in seventy years would otherwise print seventy overlapping labels.
    /// </summary>
    public static IReadOnlyList<GrowthAxisTick> YearTicks(DateOnly first, DateOnly end, float plotWidth, float minSpacing) =>
        TicksEvery(first, end, YearStride(first, end, plotWidth, minSpacing));

    /// <summary>
    /// How many years apart the labels must be so that neighbours are at least <paramref name="minSpacing"/>
    /// points apart: the plot's width per year (the x scale is <see cref="GrowthChartScale"/>'s, linear in
    /// months) rounded up to a whole number of years, and never below one. A plot with no width fits only
    /// its start, which is <see cref="int.MaxValue"/> years.
    /// </summary>
    public static int YearStride(DateOnly first, DateOnly end, float plotWidth, float minSpacing)
    {
        if (plotWidth <= 0f)
        {
            return int.MaxValue;
        }

        var pointsPerYear = plotWidth * 12.0 / GrowthChartScale.Span(first, end);
        return (int)Math.Clamp(Math.Ceiling(minSpacing / pointsPerYear), 1.0, int.MaxValue);
    }

    /// <summary>The width of the "Now" label: from just right of the dot to the chart's right edge. The
    /// chart's whole width, not the plot's — with nothing projected the dot sits on the plot's right edge,
    /// and the label runs on into the gutter rather than collapsing to nothing.</summary>
    public static float NowLabelWidth(float chartWidth, float nowX) => Math.Max(0f, chartWidth - nowX - NowLabelGap);

    private static List<GrowthAxisTick> TicksEvery(DateOnly first, DateOnly end, int strideYears)
    {
        var ticks = new List<GrowthAxisTick>();
        long years = 0;
        while (first.Year + years <= DateOnly.MaxValue.Year)
        {
            var month = first.AddYears((int)years);
            if (month > end)
            {
                break;
            }

            ticks.Add(new GrowthAxisTick(month, ChartAxis.MonthYearLabel(month)));
            years += strideYears;
        }

        return ticks.Count > 0 ? ticks : [new GrowthAxisTick(first, ChartAxis.MonthYearLabel(first))];
    }

    /// <summary>"~May 2028": the label on the hollow marker where the projection reaches the goal. Invariant
    /// culture, so the phone's language never changes the month's name.</summary>
    public static string GoalEndLabel(DateOnly date) =>
        "~" + date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
}
