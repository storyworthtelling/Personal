using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.2 drawing — the pure geometry of one signal's line: runs, dotted bridges across gaps, and the latest week's dot.</summary>
public class DigestChartTests
{
    private static DigestChart Make(params double?[] weeks) => DigestChartGeometry.Compute(weeks, 110, 44);

    [Fact]
    public void ASteadyRunIsOneLineWithADotAtTheEnd()
    {
        var chart = Make(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        var run = Assert.Single(chart.Runs);
        Assert.Equal(12, run.Count);
        Assert.Empty(chart.Bridges);
        Assert.Equal(run[^1], chart.Dot);
        Assert.Equal(0, run[0].X, 6);
        Assert.Equal(110, run[^1].X, 6);
    }

    [Fact]
    public void ARisingValueDrawsHigherUp()
    {
        var run = Assert.Single(Make(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12).Runs);
        Assert.True(run[11].Y < run[0].Y, "In the page's coordinates a larger value has a smaller Y.");
        Assert.All(run, point => Assert.InRange(point.Y, 0, 44));
    }

    [Fact]
    public void AGapIsBridgedByADottedLineNeverDrawnAsZero()
    {
        var chart = Make(5, 5, null, null, null, 6, 6, 6, 6, 6, 6, 6);
        Assert.Equal(2, chart.Runs.Count);
        var bridge = Assert.Single(chart.Bridges);
        Assert.Equal(chart.Runs[0][^1], bridge.From);
        Assert.Equal(chart.Runs[1][0], bridge.To);
        Assert.All(chart.Runs.SelectMany(run => run), point => Assert.InRange(point.Y, 0, 44));
    }

    [Fact]
    public void NoLeadingOrTrailingBridge()
    {
        var chart = Make(null, null, 5, 6, 7, 8, null, null, null, null, null, null);
        Assert.Empty(chart.Bridges);
        Assert.Null(chart.Dot);
    }

    [Fact]
    public void AFlatLineSitsInTheMiddle()
    {
        var run = Assert.Single(Make(7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7).Runs);
        Assert.All(run, point => Assert.Equal(22, point.Y, 6));
    }

    [Fact]
    public void NoReadingsDrawNothing()
    {
        var chart = Make(null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.Empty(chart.Runs);
        Assert.Empty(chart.Bridges);
        Assert.Null(chart.Dot);
    }

    [Fact]
    public void ALoneReadingIsADotNotALine()
    {
        var chart = Make(null, null, null, null, null, null, null, null, null, null, null, 9);
        Assert.Equal(chart.Dot, Assert.Single(Assert.Single(chart.Runs)));
    }
}
