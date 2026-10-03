namespace Index.Domain.Model;

/// <summary>
/// R-G21, mockup 9's Growth frame: where a month sits across a savings chart's plot, as a fraction of
/// its width. Pure, so the one place the x-scale is decided can be tested; the chart view only
/// multiplies by its pixel width.
/// </summary>
public static class GrowthChartScale
{
    /// <summary>Whole calendar months from <paramref name="earlier"/> to <paramref name="later"/>. NOT
    /// clamped: the first month is zero months in, which is what puts it at the origin.</summary>
    public static int MonthsBetween(DateOnly earlier, DateOnly later) =>
        ((later.Year - earlier.Year) * 12) + (later.Month - earlier.Month);

    /// <summary>The plot's span in months, from the first recorded month to wherever the plot ends.
    /// This is the only place the minimum of one belongs: it is a DENOMINATOR, and a one-point series
    /// has no span to divide by.</summary>
    public static int Span(DateOnly first, DateOnly end) => Math.Max(1, MonthsBetween(first, end));

    /// <summary>0 at <paramref name="first"/>, 1 at <paramref name="end"/>, evenly between.</summary>
    public static double FractionAcross(DateOnly month, DateOnly first, DateOnly end) =>
        (double)MonthsBetween(first, month) / Span(first, end);
}
