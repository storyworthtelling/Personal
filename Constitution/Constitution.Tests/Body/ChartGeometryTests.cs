using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

public class ChartGeometryTests
{
    private const double Width = 290;
    private const double Height = 100;

    private static ChartGeometry Draw(IEnumerable<BodyReading> readings, BodyWindow window = BodyWindow.Month) =>
        ChartGeometry.Compute(readings, window, BodyData.Today, Width, Height);

    private static BodyReading Sleep(int dayFromStart, double hours) =>
        BodyData.Reading(BodySignal.Sleep, BodyData.MonthStart.AddDays(dayFromStart), hours);

    [Fact]
    public void MissingDaysAreDottedBridges()
    {
        // Days 0-2, then nothing on 3 and 4, then 5-6: one line each side and one bridge across.
        var geometry = Draw([Sleep(0, 7.0), Sleep(1, 6.8), Sleep(2, 6.6), Sleep(5, 6.4), Sleep(6, 6.2)]);

        Assert.Equal(2, geometry.Lines.Count);
        Assert.Equal(3, geometry.Lines[0].Count);
        Assert.Equal(2, geometry.Lines[1].Count);
        var bridge = Assert.Single(geometry.Bridges);
        Assert.Equal(geometry.Lines[0][^1], bridge.From);
        Assert.Equal(geometry.Lines[1][0], bridge.To);
    }

    [Fact]
    public void ARunWithNoGapsIsOneLineAndNoBridges()
    {
        var geometry = Draw(Enumerable.Range(0, 30).Select(day => Sleep(day, 6.0 + (day % 3))));

        Assert.Single(geometry.Lines);
        Assert.Equal(30, geometry.Lines[0].Count);
        Assert.Empty(geometry.Bridges);
    }

    [Fact]
    public void ALoneReadingIsReachedByBridges()
    {
        var geometry = Draw([Sleep(0, 7), Sleep(1, 7.2), Sleep(4, 6.5), Sleep(7, 6.9), Sleep(8, 7.1)]);

        Assert.Equal(2, geometry.Lines.Count);
        Assert.Equal(2, geometry.Bridges.Count);
        Assert.Equal(geometry.Bridges[0].To, geometry.Bridges[1].From);
    }

    [Fact]
    public void TheDotIsTheLatestReading()
    {
        var geometry = Draw([Sleep(3, 7.0), Sleep(10, 6.0), Sleep(20, 6.5)]);

        Assert.Equal(BodyData.MonthStart.AddDays(20), geometry.LatestDay);
        Assert.Equal(6.5, geometry.LatestValue);
        Assert.NotNull(geometry.Dot);
        Assert.Equal(20.0 / 29 * Width, geometry.Dot.Value.X, 6);
    }

    [Fact]
    public void ALineStopsAtTheLastReading()
    {
        // The bed has not reported since day 22: the line ends there, the rest of the window is empty.
        var geometry = Draw(Enumerable.Range(0, 23).Select(day => Sleep(day, 6.5 + (day % 2))));

        var last = geometry.Lines.SelectMany(line => line).Max(point => point.X);
        Assert.Equal(22.0 / 29 * Width, last, 6);
        Assert.True(last < Width);
        Assert.Equal(geometry.Dot!.Value.X, last, 6);
    }

    [Fact]
    public void HigherValuesDrawHigherAndFillTheMargins()
    {
        var geometry = Draw([Sleep(0, 5.0), Sleep(1, 7.0), Sleep(2, 6.0)]);
        var line = geometry.Lines[0];
        var margin = ChartGeometry.VerticalMargin * Height;

        Assert.Equal(Height - margin, line[0].Y, 6);
        Assert.Equal(margin, line[1].Y, 6);
        Assert.Equal(Height / 2, line[2].Y, 6);
        Assert.True(line[1].Y < line[2].Y && line[2].Y < line[0].Y);
    }

    [Fact]
    public void ADaySitsAtItsPlaceAcrossTheWindow()
    {
        var geometry = Draw([Sleep(0, 6), Sleep(29, 7)]);

        var bridge = Assert.Single(geometry.Bridges);
        Assert.Equal(0, bridge.From.X, 6);
        Assert.Equal(Width, bridge.To.X, 6);
        Assert.Equal(Width, geometry.Dot!.Value.X, 6);
    }

    [Fact]
    public void AConstantSeriesDoesNotDivideByZero()
    {
        var geometry = Draw(Enumerable.Range(0, 10).Select(day => Sleep(day, 7.0)));

        var points = geometry.Lines.SelectMany(line => line).ToList();
        Assert.All(points, point => Assert.Equal(Height / 2, point.Y, 6));
        Assert.All(points, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
    }

    [Fact]
    public void ASingleReadingIsJustADot()
    {
        var geometry = Draw([Sleep(12, 6.1)]);

        Assert.Empty(geometry.Lines);
        Assert.Empty(geometry.Bridges);
        Assert.NotNull(geometry.Dot);
        Assert.Equal(Height / 2, geometry.Dot.Value.Y, 6);
        Assert.Equal(12.0 / 29 * Width, geometry.Dot.Value.X, 6);
    }

    [Fact]
    public void NothingDrawsNothing()
    {
        var geometry = Draw([]);

        Assert.Empty(geometry.Lines);
        Assert.Empty(geometry.Bridges);
        Assert.Null(geometry.Dot);
        Assert.Null(geometry.LatestDay);
        Assert.Null(geometry.LatestValue);
    }

    [Fact]
    public void TheScaleIsTheVisibleWindowOnly()
    {
        var visible = new List<BodyReading> { Sleep(0, 6.0), Sleep(1, 7.0), Sleep(2, 6.5) };
        var withOutsiders = visible.ToList();
        withOutsiders.Add(BodyData.Reading(BodySignal.Sleep, BodyData.MonthStart.AddDays(-5), 2.0));
        withOutsiders.Add(BodyData.Reading(BodySignal.Sleep, BodyData.Today.AddDays(3), 12.0));

        Assert.Equal(BodyData.Flatten(Draw(visible)), BodyData.Flatten(Draw(withOutsiders)));
        Assert.Equal(Draw(visible).LatestDay, Draw(withOutsiders).LatestDay);
    }

    [Fact]
    public void AnEarlierRangeInAWiderWindowRescales()
    {
        // The same readings in the Quarter window are scaled over the quarter, not the month.
        var readings = new List<BodyReading>
        {
            BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-60), 190),
            BodyData.Reading(BodySignal.Weight, BodyData.Today, 180),
        };

        var month = Draw(readings);
        var quarter = Draw(readings, BodyWindow.Quarter);

        Assert.Empty(month.Lines);
        Assert.Equal(Height / 2, month.Dot!.Value.Y, 6);
        Assert.Equal(Height - (ChartGeometry.VerticalMargin * Height), quarter.Dot!.Value.Y, 6);
    }

    [Theory]
    [InlineData(BodyWindow.Month, 30)]
    [InlineData(BodyWindow.Quarter, 90)]
    [InlineData(BodyWindow.Year, 365)]
    public void EveryWindowSpansItsDaysEdgeToEdge(BodyWindow window, int days)
    {
        var first = BodyData.Today.AddDays(1 - days);
        var geometry = Draw(
            [BodyData.Reading(BodySignal.Steps, first, 6000), BodyData.Reading(BodySignal.Steps, BodyData.Today, 7000)],
            window);

        Assert.Equal(Width, geometry.Dot!.Value.X, 6);
        var bridge = Assert.Single(geometry.Bridges);
        Assert.Equal(0, bridge.From.X, 6);
        Assert.Equal(Width, bridge.To.X, 6);
    }

    [Fact]
    public void ALaterReadingOfTheSameDayWins()
    {
        var geometry = Draw([Sleep(4, 5.0), Sleep(4, 7.0), Sleep(5, 6.0)]);

        var margin = ChartGeometry.VerticalMargin * Height;
        Assert.Equal(2, geometry.Lines[0].Count);
        Assert.Equal(margin, geometry.Lines[0][0].Y, 6);
        Assert.Equal(Height - margin, geometry.Lines[0][1].Y, 6);
    }

    [Fact]
    public void TheOrderReadingsArriveInDoesNotMatter()
    {
        var readings = new List<BodyReading> { Sleep(2, 6.6), Sleep(0, 7.0), Sleep(5, 6.4), Sleep(1, 6.8) };
        var shuffled = readings.AsEnumerable().Reverse().ToList();

        Assert.Equal(BodyData.Flatten(Draw(readings)), BodyData.Flatten(Draw(shuffled)));
        Assert.Equal(Draw(readings).Bridges.Count, Draw(shuffled).Bridges.Count);
    }

    [Fact]
    public void OneChartIsOneSignal()
    {
        var mixed = new List<BodyReading>
        {
            BodyData.Reading(BodySignal.Sleep, BodyData.Today, 6),
            BodyData.Reading(BodySignal.Weight, BodyData.Today, 180),
        };
        Assert.Throws<ArgumentException>(() => Draw(mixed));
    }

    [Fact]
    public void BadInputIsRefusedWithAReason()
    {
        var good = new List<BodyReading> { Sleep(0, 6) };
        var nan = new List<BodyReading> { Sleep(0, double.NaN) };

        Assert.Throws<ArgumentException>(() => Draw(nan));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartGeometry.Compute(good, BodyWindow.Month, BodyData.Today, 0, Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartGeometry.Compute(good, BodyWindow.Month, BodyData.Today, Width, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartGeometry.Compute(good, BodyWindow.Month, BodyData.Today, double.NaN, Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartGeometry.Compute(good, (BodyWindow)9, BodyData.Today, Width, Height));
        Assert.Throws<ArgumentNullException>(() => ChartGeometry.Compute(null!, BodyWindow.Month, BodyData.Today, Width, Height));
    }

    [Fact]
    public void RisingAndFallingDrawTheSameShape()
    {
        // Weight rising is worse and sleep rising is better; the line must not know the difference.
        var days = Enumerable.Range(0, 20).ToList();
        var asWeight = days.Select(day => BodyData.Reading(BodySignal.Weight, BodyData.MonthStart.AddDays(day), 180 + (day * 0.3)));
        var asSleep = days.Select(day => BodyData.Reading(BodySignal.Sleep, BodyData.MonthStart.AddDays(day), 180 + (day * 0.3)));

        Assert.Equal(BodyData.Flatten(Draw(asWeight)), BodyData.Flatten(Draw(asSleep)));
    }
}
