using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Savings;

/// <summary>
/// Guard R-G21 (INDEX-P11-REMEDIATION-PLAN.md §2.5, §10): the Growth chart's x-scale starts at the origin.
/// The bug: the month OFFSET was clamped to a minimum of 1 inside the position function (it belongs only
/// in the span's denominator, to avoid dividing by zero), so the first recorded month sat one month in
/// from the left, the first two monthly points landed on the same x (a vertical stub), and a two-point
/// line sat entirely at the right edge.
/// </summary>
public class GrowthChartScaleTests
{
    private static readonly DateOnly First = new(2025, 9, 1);

    private static DateOnly Month(int offset) => First.AddMonths(offset);

    [Fact]
    public void TheFirstRecordedMonthIsAtTheOrigin()
    {
        Assert.Equal(0d, GrowthChartScale.FractionAcross(First, First, Month(5)));
    }

    [Fact]
    public void TheLastRecordedMonthIsAtTheFullWidthWhenTheChartEndsThere()
    {
        Assert.Equal(1d, GrowthChartScale.FractionAcross(Month(5), First, Month(5)));
    }

    [Fact]
    public void ConsecutiveMonthsAreDistinctAndEvenlySpaced()
    {
        var xs = Enumerable.Range(0, 6)
            .Select(i => GrowthChartScale.FractionAcross(Month(i), First, Month(5)))
            .ToList();

        for (var i = 1; i < xs.Count; i++)
        {
            Assert.True(xs[i] > xs[i - 1], $"month {i} sits at {xs[i]}, not right of month {i - 1} at {xs[i - 1]}");
            Assert.Equal(0.2d, xs[i] - xs[i - 1], precision: 10);
        }
    }

    [Fact]
    public void ATwoPointSeriesRunsFromTheLeftEdgeToTheRightEdge()
    {
        Assert.Equal(0d, GrowthChartScale.FractionAcross(First, First, Month(1)));
        Assert.Equal(1d, GrowthChartScale.FractionAcross(Month(1), First, Month(1)));
    }

    [Fact]
    public void AOnePointSeriesIsFiniteAndAtTheOrigin()
    {
        var x = GrowthChartScale.FractionAcross(First, First, First);

        Assert.Equal(0d, x);
        Assert.False(double.IsNaN(x) || double.IsInfinity(x));
    }

    [Fact]
    public void WhenTheProjectionExtendsThePlotTheRecordedLineKeepsItsOriginAndStopsShortOfTheEnd()
    {
        // Recorded months 0..5, goal month 10: now is halfway, the goal is the right edge.
        Assert.Equal(0d, GrowthChartScale.FractionAcross(First, First, Month(10)));
        Assert.Equal(0.5d, GrowthChartScale.FractionAcross(Month(5), First, Month(10)));
        Assert.Equal(1d, GrowthChartScale.FractionAcross(Month(10), First, Month(10)));
        Assert.True(
            GrowthChartScale.FractionAcross(Month(1), First, Month(10))
            > GrowthChartScale.FractionAcross(First, First, Month(10)));
    }
}
