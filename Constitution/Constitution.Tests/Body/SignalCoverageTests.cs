using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>Review S4 and S5: a change needs real coverage in both stretches, and a day still being counted is not a day.</summary>
public class SignalCoverageTests
{
    private static List<BodyReading> Stretch(BodySignal signal, DateOnly endingOn, int days, double value) =>
        BodyData.Daily(signal, endingOn, days, value).ToList();

    [Fact]
    public void OneNightAgainstAMonthIsNoChange()
    {
        var readings = Stretch(BodySignal.Sleep, BodyData.Today.AddDays(-30), 30, 7.5);
        readings.Add(BodyData.Reading(BodySignal.Sleep, BodyData.Today, 6.5));

        Assert.Null(SignalChange.From(readings, BodySignal.Sleep, BodyWindow.Month, BodyData.Today));
    }

    [Fact]
    public void SevenDaysInEachStretchIsEnough()
    {
        var readings = Stretch(BodySignal.Sleep, BodyData.Today, 7, 6.5);
        readings.AddRange(Stretch(BodySignal.Sleep, BodyData.Today.AddDays(-30), 7, 7.5));

        var change = SignalChange.From(readings, BodySignal.Sleep, BodyWindow.Month, BodyData.Today);

        Assert.NotNull(change);
        Assert.Equal(-1.0, change.Change, 6);
    }

    [Fact]
    public void SixDaysInEitherStretchIsNotEnough()
    {
        var sixNow = Stretch(BodySignal.Sleep, BodyData.Today, 6, 6.5);
        sixNow.AddRange(Stretch(BodySignal.Sleep, BodyData.Today.AddDays(-30), 30, 7.5));
        var sixBefore = Stretch(BodySignal.Sleep, BodyData.Today, 30, 6.5);
        sixBefore.AddRange(Stretch(BodySignal.Sleep, BodyData.Today.AddDays(-30), 6, 7.5));

        Assert.Null(SignalChange.From(sixNow, BodySignal.Sleep, BodyWindow.Month, BodyData.Today));
        Assert.Null(SignalChange.From(sixBefore, BodySignal.Sleep, BodyWindow.Month, BodyData.Today));
    }

    [Fact]
    public void DaysAreCountedOncePerDay()
    {
        var readings = new List<BodyReading>();
        for (var copy = 0; copy < 10; copy++)
        {
            readings.Add(BodyData.Reading(BodySignal.Weight, BodyData.Today, 180 + copy));
            readings.Add(BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-30), 182));
        }

        Assert.Null(SignalChange.From(readings, BodySignal.Weight, BodyWindow.Month, BodyData.Today));
    }

    [Fact]
    public void TheMinimumIsSevenOrHalfAShortWindow()
    {
        Assert.Equal(7, SignalChange.MinimumDays(30));
        Assert.Equal(7, SignalChange.MinimumDays(365));
        Assert.Equal(5, SignalChange.MinimumDays(10));
        Assert.Equal(1, SignalChange.MinimumDays(1));
    }

    [Fact]
    public void TodaysStepsAreLeftOutOfTheAverage()
    {
        var readings = Stretch(BodySignal.Steps, BodyData.Today.AddDays(-1), 29, 8000);
        readings.AddRange(Stretch(BodySignal.Steps, BodyData.Today.AddDays(-30), 30, 6000));
        readings.Add(BodyData.Reading(BodySignal.Steps, BodyData.Today, 400));

        var change = SignalChange.From(readings, BodySignal.Steps, BodyWindow.Month, BodyData.Today)!;

        Assert.Equal(8000, change.Current, 6);
    }

    [Fact]
    public void TodaysActiveMinutesAreLeftOutToo()
    {
        var readings = Stretch(BodySignal.ActiveMinutes, BodyData.Today.AddDays(-1), 29, 40);
        readings.AddRange(Stretch(BodySignal.ActiveMinutes, BodyData.Today.AddDays(-30), 30, 30));
        readings.Add(BodyData.Reading(BodySignal.ActiveMinutes, BodyData.Today, 3));

        Assert.Equal(40, SignalChange.From(readings, BodySignal.ActiveMinutes, BodyWindow.Month, BodyData.Today)!.Current, 6);
    }

    [Fact]
    public void TodaysWeightStillCounts()
    {
        var readings = Stretch(BodySignal.Weight, BodyData.Today.AddDays(-1), 29, 180);
        readings.AddRange(Stretch(BodySignal.Weight, BodyData.Today.AddDays(-30), 30, 182));
        readings.Add(BodyData.Reading(BodySignal.Weight, BodyData.Today, 210));

        Assert.Equal((29 * 180.0 + 210) / 30, SignalChange.From(readings, BodySignal.Weight, BodyWindow.Month, BodyData.Today)!.Current, 6);
    }

    [Fact]
    public void OnlyRunningTotalsAreDayTotals()
    {
        Assert.True(BodySignals.IsDayTotal(BodySignal.Steps));
        Assert.True(BodySignals.IsDayTotal(BodySignal.ActiveMinutes));
        Assert.All(
            BodySignals.All.Where(signal => signal is not (BodySignal.Steps or BodySignal.ActiveMinutes)),
            signal => Assert.False(BodySignals.IsDayTotal(signal)));
    }
}
