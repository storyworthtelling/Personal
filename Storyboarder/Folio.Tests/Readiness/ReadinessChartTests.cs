using Folio.Domain.Readiness;
using Xunit;

namespace Folio.Tests.Readiness;

/// <summary>
/// The readiness chart's arithmetic, recovered from the approved frame and pinned here.
///
/// <para>The frame plots three drafts at x = 32, 111, 190 on a baseline at y = 100, and every one of
/// its twelve points satisfies <c>y = 100 − count × 6</c> — Must 14/9/5 at 16/46/70, Great 3/7/11 at
/// 82/58/34, and so on. Six pixels a finding is not a constant, though: it is what
/// <c>(baseline − headroom) / 14</c> comes to when the largest count on the chart is 14. So the
/// scale is derived, and it reproduces the drawing.</para>
/// </summary>
public class ReadinessChartTests
{
    private static readonly DraftFindings[] Frame =
    [
        new(1, new Dictionary<ReadinessTier, int>
        {
            [ReadinessTier.Must] = 14, [ReadinessTier.Should] = 11,
            [ReadinessTier.Consider] = 6, [ReadinessTier.Great] = 3,
        }),
        new(2, new Dictionary<ReadinessTier, int>
        {
            [ReadinessTier.Must] = 9, [ReadinessTier.Should] = 12,
            [ReadinessTier.Consider] = 9, [ReadinessTier.Great] = 7,
        }),
        new(3, new Dictionary<ReadinessTier, int>
        {
            [ReadinessTier.Must] = 5, [ReadinessTier.Should] = 8,
            [ReadinessTier.Consider] = 10, [ReadinessTier.Great] = 11,
        }),
    ];

    // G9: the fourth tier is what makes readiness show improvement. Musts falling is only half the
    // picture; without Great rising, a book that got better looks like a book with fewer problems.
    [Fact]
    public void SeveritySpansFourTiers()
    {
        Assert.Equal(4, Enum.GetValues<ReadinessTier>().Length);
        Assert.Contains(ReadinessTier.Great, Enum.GetValues<ReadinessTier>());
    }

    [Theory]
    [InlineData(ReadinessTier.Must, new[] { 16.0, 46.0, 70.0 })]
    [InlineData(ReadinessTier.Should, new[] { 34.0, 28.0, 52.0 })]
    [InlineData(ReadinessTier.Consider, new[] { 64.0, 46.0, 40.0 })]
    [InlineData(ReadinessTier.Great, new[] { 82.0, 58.0, 34.0 })]
    public void EveryPointLandsWhereTheFrameDrewIt(ReadinessTier tier, double[] expected)
    {
        var series = Assert.Single(ReadinessChart.Plot(Frame).Series, s => s.Tier == tier);

        Assert.Equal(expected, series.Points.Select(point => Math.Round(point.Y, 1)));
    }

    [Fact]
    public void DraftsSitWhereTheFrameDrewThem()
    {
        var plotted = ReadinessChart.Plot(Frame);

        Assert.Equal([32.0, 111.0, 190.0], plotted.Series[0].Points.Select(point => Math.Round(point.X, 1)));
    }

    /// <summary>
    /// The form is a line chart <b>because the colour check failed as fills</b>: run as four adjacent
    /// stacked areas, Must and Should measure ΔE 12.6 in normal vision, under the floor of 15, and
    /// reordering made it worse. So nothing here may describe a filled series — identity rests on
    /// path, position and a direct label, never on colour alone.
    /// </summary>
    [Fact]
    public void NoSeriesIsFilled()
    {
        var members = typeof(ReadinessSeries).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(members, name =>
            name.Contains("Fill", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Area", StringComparison.OrdinalIgnoreCase));
    }

    // Every line is named at its own end. Two endpoints that land close together would print their
    // names on top of each other, so the labels are pushed apart and a leader bends out to them.
    [Fact]
    public void EndLabelsArePushedApartRatherThanOverlapping()
    {
        var labels = ReadinessChart.Plot(Frame).Series
            .Select(series => series.LabelY)
            .OrderBy(y => y)
            .ToList();

        for (var i = 1; i < labels.Count; i++)
        {
            Assert.True(labels[i] - labels[i - 1] >= ReadinessChart.MinimumLabelGap,
                $"Two end labels are {labels[i] - labels[i - 1]:F1} apart, under the "
                + $"{ReadinessChart.MinimumLabelGap} a line of type needs.");
        }
    }

    /// <summary>
    /// The frame's own four label positions, which is what makes the packing rule above the rule
    /// that was drawn rather than a plausible one. The lines end at 34, 40, 52 and 70; the names
    /// print at 34, 47, 60 and 73 — each pushed to exactly one gap below the one before it.
    /// </summary>
    [Theory]
    [InlineData(ReadinessTier.Great, 34.0)]
    [InlineData(ReadinessTier.Consider, 47.0)]
    [InlineData(ReadinessTier.Should, 60.0)]
    [InlineData(ReadinessTier.Must, 73.0)]
    public void EndLabelsLandWhereTheFramePrintedThem(ReadinessTier tier, double expected)
    {
        var series = Assert.Single(ReadinessChart.Plot(Frame).Series, s => s.Tier == tier);

        Assert.Equal(expected, Math.Round(series.LabelY, 1));
    }

    [Fact]
    public void ALabelStaysOnTheLineItNamesWhenNothingCrowdsIt()
    {
        var lonely = new[]
        {
            new DraftFindings(1, new Dictionary<ReadinessTier, int> { [ReadinessTier.Must] = 10 }),
            new DraftFindings(2, new Dictionary<ReadinessTier, int> { [ReadinessTier.Must] = 4 }),
        };

        var series = Assert.Single(ReadinessChart.Plot(lonely).Series);
        Assert.Equal(series.Points[^1].Y, series.LabelY, 1);
    }

    [Fact]
    public void ASingleDraftStillPlots()
    {
        var one = new[]
        {
            new DraftFindings(1, new Dictionary<ReadinessTier, int> { [ReadinessTier.Must] = 3 }),
        };

        Assert.Single(Assert.Single(ReadinessChart.Plot(one).Series).Points);
    }

    [Fact]
    public void NoDraftsPlotsNothing()
    {
        Assert.Empty(ReadinessChart.Plot([]).Series);
    }

    // A book with no findings at all must not divide by zero on its way to a flat line.
    [Fact]
    public void ABookWithNoFindingsIsFlatOnTheBaseline()
    {
        var clean = new[]
        {
            new DraftFindings(1, new Dictionary<ReadinessTier, int> { [ReadinessTier.Must] = 0 }),
            new DraftFindings(2, new Dictionary<ReadinessTier, int> { [ReadinessTier.Must] = 0 }),
        };

        var series = Assert.Single(ReadinessChart.Plot(clean).Series);
        Assert.All(series.Points, point => Assert.Equal(ReadinessChart.BaselineY, point.Y, 1));
    }
}
