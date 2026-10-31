using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

public class SignalOrderTests
{
    private static List<BodySignal> Order(params SignalChange[] changes) =>
        SignalOrder.Order(changes).Select(change => change.Signal).ToList();

    [Fact]
    public void WorseningFirst()
    {
        // Steps moved the most, but they rose (better); sleep slipped, so sleep leads.
        var sleep = BodyData.Change(BodySignal.Sleep, current: 6.3333, previous: 7.0);
        var steps = BodyData.Change(BodySignal.Steps, current: 7400, previous: 5900);
        var weight = BodyData.Change(BodySignal.Weight, current: 179.2, previous: 181.4);
        var heart = BodyData.Change(BodySignal.RestingHeartRate, current: 59, previous: 63);

        Assert.Equal(
            [BodySignal.Sleep, BodySignal.Steps, BodySignal.Weight, BodySignal.RestingHeartRate],
            Order(heart, weight, steps, sleep));
    }

    [Fact]
    public void RisingWeightIsWorsening()
    {
        // The same size of move, either way, and a big sleep gain: only the rising weight leads.
        var rising = BodyData.Change(BodySignal.Weight, current: 183.0, previous: 180.0);
        var sleepGain = BodyData.Change(BodySignal.Sleep, current: 8.0, previous: 7.0);
        Assert.True(rising.IsWorsening);
        Assert.Equal([BodySignal.Weight, BodySignal.Sleep], Order(sleepGain, rising));

        var falling = BodyData.Change(BodySignal.Weight, current: 177.0, previous: 180.0);
        Assert.False(falling.IsWorsening);
        Assert.Equal([BodySignal.Sleep, BodySignal.Weight], Order(falling, sleepGain));
    }

    [Fact]
    public void WorseningSignalsGoByTheSizeOfTheMove()
    {
        var small = BodyData.Change(BodySignal.Weight, current: 182.0, previous: 180.0);
        var big = BodyData.Change(BodySignal.Steps, current: 4000, previous: 7000);
        var medium = BodyData.Change(BodySignal.RestingHeartRate, current: 67, previous: 62);

        Assert.Equal([BodySignal.Steps, BodySignal.RestingHeartRate, BodySignal.Weight], Order(small, medium, big));
    }

    [Fact]
    public void ThenTheRestByTheSizeOfTheMove()
    {
        var bigGain = BodyData.Change(BodySignal.Steps, current: 9000, previous: 6000);
        var smallGain = BodyData.Change(BodySignal.ActiveMinutes, current: 36, previous: 30);
        var bigDrop = BodyData.Change(BodySignal.Weight, current: 175.0, previous: 180.0);

        Assert.Equal(
            [BodySignal.Steps, BodySignal.Weight, BodySignal.ActiveMinutes],
            Order(smallGain, bigDrop, bigGain));
    }

    [Fact]
    public void ASteadySignalNeverLeads()
    {
        var wobblingUp = BodyData.Change(BodySignal.Weight, current: 180.4, previous: 180.0);
        var gain = BodyData.Change(BodySignal.Steps, current: 7600, previous: 7000);

        Assert.True(wobblingUp.IsSteady);
        Assert.Equal([BodySignal.Steps, BodySignal.Weight], Order(wobblingUp, gain));
    }

    [Fact]
    public void ANeutralSignalIsNeverCalledWorsening()
    {
        var calories = BodyData.Change(BodySignal.Calories, current: 3200, previous: 2000);
        var slip = BodyData.Change(BodySignal.Sleep, current: 6.5, previous: 7.0);

        Assert.False(calories.IsWorsening);
        Assert.Equal([BodySignal.Sleep, BodySignal.Calories], Order(calories, slip));
    }

    [Fact]
    public void TiesKeepTheOrderTheyArrivedIn()
    {
        var a = BodyData.Change(BodySignal.Weight, current: 182.0, previous: 180.0);
        var b = BodyData.Change(BodySignal.RestingHeartRate, current: 66, previous: 62);
        Assert.Equal(a.Movement, b.Movement);

        Assert.Equal([BodySignal.Weight, BodySignal.RestingHeartRate], Order(a, b));
        Assert.Equal([BodySignal.RestingHeartRate, BodySignal.Weight], Order(b, a));
    }

    [Fact]
    public void NothingIsDroppedOrInvented()
    {
        Assert.Empty(SignalOrder.Order([]));
        var changes = BodySignals.All.Select(signal => BodyData.Change(signal, 100, 90)).ToList();
        Assert.Equal(changes.Count, SignalOrder.Order(changes).Count);
        Assert.Throws<ArgumentNullException>(() => SignalOrder.Order(null!));
    }
}
