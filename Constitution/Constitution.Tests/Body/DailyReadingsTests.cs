using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>Samples become one reading a day, in the signal's canonical unit, and two sources are never added together.</summary>
public class DailyReadingsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Tuesday = new(2026, 11, 17);

    [Fact]
    public void WeighInsOfADayAreAveraged()
    {
        var readings = DailyReadings.From(BodySignal.Weight, [Samples.Of(Monday, 180), Samples.Of(Monday, 182), Samples.Of(Tuesday, 181)]);

        Assert.Equal([new BodyReading(BodySignal.Weight, Monday, 181), new BodyReading(BodySignal.Weight, Tuesday, 181)], readings);
    }

    [Fact]
    public void StepsFromTwoSourcesAreNotAddedTogether()
    {
        // The phone and the watch both counted the same walk; the day is the fuller count, not the sum.
        var readings = DailyReadings.From(BodySignal.Steps,
        [
            Samples.Of(Monday, 4000, "example.phone"), Samples.Of(Monday, 3000, "example.phone"),
            Samples.Of(Monday, 6500, "example.watch"),
        ]);

        Assert.Equal(7000, Assert.Single(readings).Value);
    }

    [Fact]
    public void ASourcesOwnRecordsInADayAreAdded()
    {
        var readings = DailyReadings.From(BodySignal.ActiveMinutes, [Samples.Of(Monday, 20), Samples.Of(Monday, 25)]);

        Assert.Equal(45, Assert.Single(readings).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NothingUsableIsNoReadingRatherThanAZero(double amount)
    {
        Assert.Empty(DailyReadings.From(BodySignal.Sleep, [Samples.Of(Monday, amount)]));
    }

    [Fact]
    public void ReadingsComeOldestFirst()
    {
        var readings = DailyReadings.From(BodySignal.RestingHeartRate, [Samples.Of(Tuesday, 60), Samples.Of(Monday, 62)]);

        Assert.Equal([Monday, Tuesday], readings.Select(reading => reading.Day));
    }

    [Fact]
    public void ADerivedSignalIsNotReadFromHealthConnect()
    {
        Assert.Throws<ArgumentException>(() => DailyReadings.From(BodySignal.Strength, [Samples.Of(Monday, 100)]));
    }

    [Fact]
    public void EverySignalHealthConnectSuppliesIsHandled()
    {
        Assert.All(BodySignals.HealthConnect, signal => Assert.Single(DailyReadings.From(signal, [Samples.Of(Monday, 1)])));
    }

    [Fact]
    public void KilogramsBecomePoundsAndSecondsBecomeHours()
    {
        Assert.Equal(2.2046, BodyUnits.PoundsFromGrams(1000), 4);
        Assert.Equal(7.5, BodyUnits.HoursFromSeconds(27000), 6);
        Assert.Equal(0, BodyUnits.PoundsFromGrams(0));
    }
}
