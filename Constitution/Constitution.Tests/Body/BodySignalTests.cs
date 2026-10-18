using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>
/// The signals Body charts, and what is frozen about them. The ids are typed out as literals on
/// purpose (the same reason as <c>ConstitutionInvariantsTests</c>): a signal's id is what the
/// encrypted Body backup stores, so a harmless-looking rename of the enum member must fail here
/// rather than silently orphan every stored reading.
/// </summary>
public class BodySignalTests
{
    private static readonly (BodySignal Signal, string Id)[] FrozenIds =
    [
        (BodySignal.Weight, "weight"),
        (BodySignal.BodyComposition, "body-composition"),
        (BodySignal.RestingHeartRate, "resting-heart-rate"),
        (BodySignal.HeartRateVariability, "heart-rate-variability"),
        (BodySignal.Steps, "steps"),
        (BodySignal.ActiveMinutes, "active-minutes"),
        (BodySignal.Sleep, "sleep"),
        (BodySignal.Strength, "strength"),
        (BodySignal.Calories, "calories"),
    ];

    [Fact]
    public void IdsAreFrozenLiterals()
    {
        foreach (var (signal, id) in FrozenIds)
        {
            Assert.Equal(id, BodySignals.Id(signal));
        }
    }

    [Fact]
    public void EverySignalHasAFrozenId()
    {
        var all = Enum.GetValues<BodySignal>();
        Assert.Equal(all.Length, FrozenIds.Length);
        Assert.Equal(all.OrderBy(signal => signal), FrozenIds.Select(pair => pair.Signal).OrderBy(signal => signal));
    }

    [Fact]
    public void IdsRoundTripAndUnknownOnesAreRefused()
    {
        foreach (var (signal, id) in FrozenIds)
        {
            Assert.True(BodySignals.TryFromId(id, out var read));
            Assert.Equal(signal, read);
        }

        Assert.False(BodySignals.TryFromId("blood-pressure", out _));
        Assert.False(BodySignals.TryFromId("Weight", out _));
        Assert.False(BodySignals.TryFromId("", out _));
        Assert.False(BodySignals.TryFromId(null, out _));
    }

    [Fact]
    public void SevenFromHealthConnectTwoDerived()
    {
        Assert.Equal(
            [
                BodySignal.Weight, BodySignal.BodyComposition, BodySignal.RestingHeartRate,
                BodySignal.HeartRateVariability, BodySignal.Steps, BodySignal.ActiveMinutes, BodySignal.Sleep,
            ],
            BodySignals.HealthConnect);
        Assert.Equal([BodySignal.Strength, BodySignal.Calories], BodySignals.Derived);
        Assert.Equal(9, BodySignals.All.Count);
        Assert.Equal(BodySignals.All.Count, BodySignals.All.Distinct().Count());
        Assert.Empty(BodySignals.HealthConnect.Intersect(BodySignals.Derived));
        Assert.Equal(Enum.GetValues<BodySignal>().OrderBy(s => s), BodySignals.All.OrderBy(s => s));
    }

    [Fact]
    public void RisingMeansWhatThePlanSays()
    {
        Assert.Equal(RisingIs.Worse, BodySignals.Rising(BodySignal.Weight));
        Assert.Equal(RisingIs.Worse, BodySignals.Rising(BodySignal.RestingHeartRate));
        Assert.Equal(RisingIs.Worse, BodySignals.Rising(BodySignal.BodyComposition));
        Assert.Equal(RisingIs.Better, BodySignals.Rising(BodySignal.Sleep));
        Assert.Equal(RisingIs.Better, BodySignals.Rising(BodySignal.HeartRateVariability));
        Assert.Equal(RisingIs.Better, BodySignals.Rising(BodySignal.Steps));
        Assert.Equal(RisingIs.Better, BodySignals.Rising(BodySignal.ActiveMinutes));
        Assert.Equal(RisingIs.Better, BodySignals.Rising(BodySignal.Strength));
        Assert.Equal(RisingIs.Neutral, BodySignals.Rising(BodySignal.Calories));
    }

    [Fact]
    public void EverySignalIsNamedAndHasUnits()
    {
        foreach (var signal in BodySignals.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(BodySignals.Name(signal)));
            Assert.False(string.IsNullOrWhiteSpace(BodySignals.Units(signal)));
        }

        Assert.Equal("Sleep", BodySignals.Name(BodySignal.Sleep));
        Assert.Equal("hours", BodySignals.Units(BodySignal.Sleep));
        Assert.Equal("lb", BodySignals.Units(BodySignal.Weight));
        Assert.Equal("bpm", BodySignals.Units(BodySignal.RestingHeartRate));
    }

    [Fact]
    public void UndefinedSignalsAreRefused()
    {
        var bogus = (BodySignal)99;
        Assert.Throws<ArgumentOutOfRangeException>(() => BodySignals.Id(bogus));
        Assert.Throws<ArgumentOutOfRangeException>(() => BodySignals.Rising(bogus));
        Assert.Throws<ArgumentOutOfRangeException>(() => BodySignals.Name(bogus));
        Assert.Throws<ArgumentOutOfRangeException>(() => BodySignals.Units(bogus));
    }
}
