using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

public class SignalChangeTests
{
    [Fact]
    public void ComparesThisMonthToThePreviousOne()
    {
        var readings = BodyData.TwoMonths(BodySignal.Sleep, previous: 7.0, current: 6.5);

        var change = SignalChange.From(readings, BodySignal.Sleep, BodyWindow.Month, BodyData.Today);

        Assert.NotNull(change);
        Assert.Equal(BodySignal.Sleep, change.Signal);
        Assert.Equal(6.5, change.Current, 6);
        Assert.Equal(7.0, change.Previous, 6);
        Assert.Equal(-0.5, change.Change, 6);
    }

    [Fact]
    public void AveragesTheDaysOfEachPeriod()
    {
        var readings = new List<BodyReading>
        {
            BodyData.Reading(BodySignal.Weight, BodyData.Today, 180),
            BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-10), 178),
            BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-35), 184),
            BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-40), 182),
        };

        var change = SignalChange.From(readings, BodySignal.Weight, BodyWindow.Month, BodyData.Today)!;

        Assert.Equal(179, change.Current, 6);
        Assert.Equal(183, change.Previous, 6);
    }

    [Fact]
    public void ThereIsNoChangeWithoutAPreviousPeriod()
    {
        var onlyThisMonth = BodyData.Daily(BodySignal.Steps, BodyData.Today, 30, 7000);
        var onlyBefore = BodyData.Daily(BodySignal.Steps, BodyData.Today.AddDays(-30), 30, 7000);

        Assert.Null(SignalChange.From(onlyThisMonth, BodySignal.Steps, BodyWindow.Month, BodyData.Today));
        Assert.Null(SignalChange.From(onlyBefore, BodySignal.Steps, BodyWindow.Month, BodyData.Today));
        Assert.Null(SignalChange.From([], BodySignal.Steps, BodyWindow.Month, BodyData.Today));
    }

    [Fact]
    public void OtherSignalsAndOutOfRangeDaysAreIgnored()
    {
        var readings = BodyData.TwoMonths(BodySignal.Steps, previous: 6000, current: 7000);
        readings.AddRange(BodyData.TwoMonths(BodySignal.Weight, previous: 190, current: 170));
        readings.Add(BodyData.Reading(BodySignal.Steps, BodyData.Today.AddDays(1), 99999));
        readings.Add(BodyData.Reading(BodySignal.Steps, BodyData.Today.AddDays(-61), 1));

        var change = SignalChange.From(readings, BodySignal.Steps, BodyWindow.Month, BodyData.Today)!;

        Assert.Equal(7000, change.Current, 6);
        Assert.Equal(6000, change.Previous, 6);
    }

    [Fact]
    public void ALaterReadingOfTheSameDayWins()
    {
        var readings = new List<BodyReading>
        {
            BodyData.Reading(BodySignal.Weight, BodyData.Today, 181),
            BodyData.Reading(BodySignal.Weight, BodyData.Today, 179),
            BodyData.Reading(BodySignal.Weight, BodyData.Today.AddDays(-30), 183),
        };

        var change = SignalChange.From(readings, BodySignal.Weight, BodyWindow.Month, BodyData.Today)!;

        Assert.Equal(179, change.Current, 6);
    }

    [Fact]
    public void QuarterComparesNinetyDayStretches()
    {
        var readings = BodyData.Daily(BodySignal.Sleep, BodyData.Today, 90, 6.0).ToList();
        readings.AddRange(BodyData.Daily(BodySignal.Sleep, BodyData.Today.AddDays(-90), 90, 7.0));

        var change = SignalChange.From(readings, BodySignal.Sleep, BodyWindow.Quarter, BodyData.Today)!;

        Assert.Equal(6.0, change.Current, 6);
        Assert.Equal(7.0, change.Previous, 6);
    }

    [Fact]
    public void MovementIsCountedInSteadyBands()
    {
        // Sleep is steady inside a quarter hour, so forty minutes is about two and two-thirds bands.
        var sleep = BodyData.Change(BodySignal.Sleep, current: 6.5, previous: 6.5 + (40.0 / 60));
        Assert.Equal(40.0 / 60 / 0.25, sleep.Movement, 6);

        var weight = BodyData.Change(BodySignal.Weight, current: 179.2, previous: 181.4);
        Assert.Equal(2.2, weight.Movement, 6);
    }

    [Fact]
    public void AWobbleIsSteadyAndNeverWorsening()
    {
        var wobble = BodyData.Change(BodySignal.Weight, current: 180.6, previous: 180.0);
        Assert.True(wobble.IsSteady);
        Assert.False(wobble.IsWorsening);

        var move = BodyData.Change(BodySignal.Weight, current: 182.0, previous: 180.0);
        Assert.False(move.IsSteady);
        Assert.True(move.IsWorsening);
    }

    [Fact]
    public void WorseningFollowsWhichWayRisingIs()
    {
        Assert.True(BodyData.Change(BodySignal.Weight, 182, 180).IsWorsening);
        Assert.False(BodyData.Change(BodySignal.Weight, 178, 180).IsWorsening);
        Assert.True(BodyData.Change(BodySignal.RestingHeartRate, 64, 60).IsWorsening);
        Assert.True(BodyData.Change(BodySignal.BodyComposition, 22.5, 21.0).IsWorsening);
        Assert.True(BodyData.Change(BodySignal.Sleep, 6.0, 7.0).IsWorsening);
        Assert.False(BodyData.Change(BodySignal.Sleep, 7.5, 7.0).IsWorsening);
        Assert.True(BodyData.Change(BodySignal.HeartRateVariability, 30, 40).IsWorsening);
        Assert.True(BodyData.Change(BodySignal.Steps, 5000, 7000).IsWorsening);
        Assert.False(BodyData.Change(BodySignal.Calories, 3000, 2000).IsWorsening);
        Assert.False(BodyData.Change(BodySignal.Calories, 1000, 2000).IsWorsening);
    }

    [Fact]
    public void NonsenseIsRefusedWithAReason()
    {
        Assert.Throws<ArgumentException>(() => new SignalChange(BodySignal.Steps, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => new SignalChange(BodySignal.Steps, 1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalChange((BodySignal)42, 1, 1));
        var bad = new List<BodyReading> { BodyData.Reading(BodySignal.Steps, BodyData.Today, double.NaN) };
        Assert.Throws<ArgumentException>(() => SignalChange.From(bad, BodySignal.Steps, BodyWindow.Month, BodyData.Today));
    }
}
