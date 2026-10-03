using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Savings;

/// <summary>
/// Phase 4 "Charts" (mockup 9, frame 2): the Growth chart's y axis is a few round money steps labelled in a
/// gutter beside the plot ("5k / 10k / 15k" under a $17,500 goal), and its x axis is labelled once a year
/// from the first recorded month ("Sep '25 / Sep '26 / Sep '27"), not just at its two ends. The strings
/// are <see cref="ChartAxis.MonthYearLabel"/>'s — the SAME apostrophe-bearing format as Snowball's axis, whose
/// literal (<c>''yy</c>) prints no apostrophe at all in a .NET format string.
/// </summary>
public class GrowthChartAxisTests
{
    [Fact]
    public void AGoalOfSeventeenThousandFiveHundredGetsFiveTenAndFifteenThousand()
    {
        Assert.Equal([5_000m, 10_000m, 15_000m], GrowthChartAxis.MoneySteps(17_500m));
    }

    [Fact]
    public void TheTopOfTheScaleIsNeverAStep_ItIsTheGoalLineOrTheHighestBalanceAndLabelledAsSuch()
    {
        Assert.Equal([5_000m, 10_000m, 15_000m], GrowthChartAxis.MoneySteps(20_000m));
    }

    [Fact]
    public void ALabelledStepReadsAsAShortMoneyTick()
    {
        Assert.Equal(["5k", "10k", "15k"], GrowthChartAxis.MoneySteps(17_500m).Select(ChartAxis.MoneyTick));
    }

    [Fact]
    public void AScaleTooSmallForAnyStepHasNone()
    {
        Assert.Empty(GrowthChartAxis.MoneySteps(0m));
    }

    [Fact]
    public void TheXAxisIsLabelledOnceAYearFromTheFirstRecordedMonth()
    {
        var ticks = GrowthChartAxis.YearTicks(new DateOnly(2025, 9, 1), new DateOnly(2028, 5, 1));

        Assert.Equal(["Sep '25", "Sep '26", "Sep '27"], ticks.Select(t => t.Label));
        Assert.Equal(
            [new DateOnly(2025, 9, 1), new DateOnly(2026, 9, 1), new DateOnly(2027, 9, 1)],
            ticks.Select(t => t.Month));
    }

    [Fact]
    public void AnAnniversaryFallingExactlyOnTheEndOfThePlotIsLabelled()
    {
        var ticks = GrowthChartAxis.YearTicks(new DateOnly(2025, 9, 1), new DateOnly(2027, 9, 1));

        Assert.Equal(["Sep '25", "Sep '26", "Sep '27"], ticks.Select(t => t.Label));
    }

    [Fact]
    public void ARangeUnderAYearIsLabelledAtItsStartOnly()
    {
        var ticks = GrowthChartAxis.YearTicks(new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 1));

        Assert.Equal(["Mar '26"], ticks.Select(t => t.Label));
    }

    [Fact]
    public void TheMonthLabelCarriesItsApostrophe()
    {
        // The Savings chart used to format with "MMM ''yy", whose '' is an empty literal in a .NET custom
        // format: it prints "Sep 25". The label is ChartAxis's, which SnowballChartMathTests pins.
        Assert.Equal("Sep '25", ChartAxis.MonthYearLabel(new DateOnly(2025, 9, 1)));
        Assert.Equal("Sep 25", new DateOnly(2025, 9, 1).ToString("MMM ''yy", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheProjectedEndIsLabelledWithItsMonthAndYear_InTheInvariantCulture()
    {
        Assert.Equal("~May 2028", GrowthChartAxis.GoalEndLabel(new DateOnly(2028, 5, 1)));

        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("~May 2028", GrowthChartAxis.GoalEndLabel(new DateOnly(2028, 5, 1)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    // ---- thinning: the label count follows the horizon, the label room follows the plot ----

    private const float PlotWidth = 260f;
    private const float Spacing = 96f;

    private static float XFor(GrowthAxisTick tick, DateOnly first, DateOnly end) =>
        (float)GrowthChartScale.FractionAcross(tick.Month, first, end) * PlotWidth;

    [Fact]
    public void AShortHorizonKeepsEveryYearWhenThereIsRoomForThem()
    {
        var first = new DateOnly(2025, 9, 1);
        var end = new DateOnly(2027, 9, 1);

        var ticks = GrowthChartAxis.YearTicks(first, end, PlotWidth, Spacing);

        Assert.Equal(["Sep '25", "Sep '26", "Sep '27"], ticks.Select(t => t.Label));
    }

    [Fact]
    public void ASeventyThreeYearHorizonIsThinnedSoNoTwoLabelsCloserThanTheSpacing()
    {
        var first = new DateOnly(2025, 9, 1);
        var end = first.AddYears(73);

        var ticks = GrowthChartAxis.YearTicks(first, end, PlotWidth, Spacing);

        Assert.True(ticks.Count <= (int)(PlotWidth / Spacing) + 1, $"{ticks.Count} labels for a {PlotWidth}pt plot");
        Assert.True(ticks.Count > 1);
        Assert.Equal(first, ticks[0].Month);
        for (var i = 1; i < ticks.Count; i++)
        {
            Assert.True(XFor(ticks[i], first, end) - XFor(ticks[i - 1], first, end) >= Spacing - 0.001f);
            Assert.True(ticks[i].Month <= end);
        }
        Assert.Equal(ticks.Count, ticks.Select(t => t.Label).Distinct().Count());
    }

    [Fact]
    public void TheStrideIsWholeYearsAndAtLeastOne()
    {
        var first = new DateOnly(2025, 9, 1);

        Assert.Equal(1, GrowthChartAxis.YearStride(first, first.AddYears(2), PlotWidth, Spacing));
        Assert.Equal(1, GrowthChartAxis.YearStride(first, first, PlotWidth, Spacing));
        Assert.Equal(27, GrowthChartAxis.YearStride(first, first.AddYears(73), PlotWidth, Spacing));
    }

    [Fact]
    public void AHorizonThatWouldPassYearNineThousandNineHundredNinetyNineDoesNotThrow()
    {
        var first = new DateOnly(2025, 9, 1);
        var end = DateOnly.MaxValue;

        var ticks = GrowthChartAxis.YearTicks(first, end, PlotWidth, Spacing);
        var everyYear = GrowthChartAxis.YearTicks(first, end);

        Assert.NotEmpty(ticks);
        Assert.True(ticks.All(t => t.Month <= end));
        Assert.True(everyYear.Count > ticks.Count);
        Assert.Equal(first, ticks[0].Month);
    }

    [Fact]
    public void ARangeStartingInTheLastYearOfTheCalendarHasItsStartOnly()
    {
        var first = new DateOnly(9999, 3, 1);

        var ticks = GrowthChartAxis.YearTicks(first, DateOnly.MaxValue, PlotWidth, Spacing);

        Assert.Equal([first], ticks.Select(t => t.Month));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void APlotWithNoWidthIsLabelledAtItsStartOnly(float width)
    {
        var first = new DateOnly(2025, 9, 1);

        var ticks = GrowthChartAxis.YearTicks(first, first.AddYears(10), width, Spacing);

        Assert.Equal([first], ticks.Select(t => t.Month));
    }

    // ---- "Now" ----

    [Fact]
    public void TheNowLabelOfAGoalMetOrOutOfPaceChartStillHasTheGutterToWriteIn()
    {
        // No projection is drawn, so "Now" sits at the plot's right edge (260) in a 300pt-wide chart:
        // the label runs on into the 40pt gutter, as it did before the plot width was subtracted twice.
        Assert.Equal(34f, GrowthChartAxis.NowLabelWidth(300f, 260f), 0.001f);
    }

    [Fact]
    public void TheNowLabelOfAChartWithAProjectionHasTheRoomLeftToTheRightOfTheDot()
    {
        Assert.Equal(104f, GrowthChartAxis.NowLabelWidth(300f, 190f), 0.001f);
    }

    [Fact]
    public void TheNowLabelWidthIsNeverNegative()
    {
        Assert.Equal(0f, GrowthChartAxis.NowLabelWidth(300f, 299f), 0.001f);
        Assert.Equal(0f, GrowthChartAxis.NowLabelWidth(300f, 400f), 0.001f);
    }
}
