using Constitution.Domain.Workouts;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The reading of one forecast hour, and of the hours a slot touches: where bad begins, which cause is named when several
/// apply, and what a missing or unreadable forecast is taken to mean.
/// </summary>
public class ConditionsAssessmentTests
{
    private static readonly ConditionsLimits Limits = new();

    private static HourConditions Hour(
        double temperature = 60,
        int chance = 0,
        PrecipitationKind kind = PrecipitationKind.None,
        int? air = 30,
        bool daylight = true) =>
        new(At(1, 17), temperature, chance, kind, air, daylight);

    // ---- wet ----

    [Theory]
    [InlineData(49, null)]
    [InlineData(50, ConditionCause.Rain)]
    [InlineData(100, ConditionCause.Rain)]
    public void RainCountsFromTheChanceUp(int chance, ConditionCause? expected) =>
        Assert.Equal(expected, ConditionsAssessment.Of(Hour(chance: chance, kind: PrecipitationKind.Rain), Limits));

    [Fact]
    public void ACertainTypeWithNoChanceIsNothing() =>
        Assert.Null(ConditionsAssessment.Of(Hour(chance: 0, kind: PrecipitationKind.Rain), Limits));

    [Fact]
    public void AChanceWithNoTypeIsReadAsRain() =>
        Assert.Equal(ConditionCause.Rain, ConditionsAssessment.Of(Hour(chance: 90), Limits));

    [Fact]
    public void SnowIsSnow() =>
        Assert.Equal(ConditionCause.Snow, ConditionsAssessment.Of(Hour(temperature: 28, chance: 60, kind: PrecipitationKind.Snow), Limits));

    [Fact]
    public void FreezingRainAndSleetAreIce() =>
        Assert.Equal(ConditionCause.Ice, ConditionsAssessment.Of(Hour(temperature: 31, chance: 60, kind: PrecipitationKind.Ice), Limits));

    [Theory]
    [InlineData(32, ConditionCause.Ice)]
    [InlineData(33, ConditionCause.Rain)]
    public void RainAtFreezingIsIce(double temperature, ConditionCause expected) =>
        Assert.Equal(expected, ConditionsAssessment.Of(Hour(temperature: temperature, chance: 70, kind: PrecipitationKind.Rain), Limits));

    // ---- heat ----

    [Theory]
    [InlineData(89.9, null)]
    [InlineData(90, ConditionCause.Heat)]
    [InlineData(104, ConditionCause.Heat)]
    public void HeatCountsFromNinetyUp(double temperature, ConditionCause? expected) =>
        Assert.Equal(expected, ConditionsAssessment.Of(Hour(temperature: temperature), Limits));

    [Fact]
    public void ColdAloneIsNotABadHour() =>
        Assert.Null(ConditionsAssessment.Of(Hour(temperature: 5), Limits));

    // ---- air ----

    [Theory]
    [InlineData(100, null)]
    [InlineData(101, ConditionCause.PoorAir)]
    [InlineData(300, ConditionCause.PoorAir)]
    public void AirIsPoorAboveOneHundred(int index, ConditionCause? expected) =>
        Assert.Equal(expected, ConditionsAssessment.Of(Hour(air: index), Limits));

    [Fact]
    public void AnAirReadingNobodyTookIsNotPoorAir() =>
        Assert.Null(ConditionsAssessment.Of(Hour(air: null), Limits));

    // ---- darkness ----

    [Fact]
    public void DarknessMattersOnlyWhenAskedFor()
    {
        var night = Hour(daylight: false);

        Assert.Null(ConditionsAssessment.Of(night, Limits));
        Assert.Equal(ConditionCause.Darkness, ConditionsAssessment.Of(night, new ConditionsLimits { AvoidDarkness = true }));
        Assert.Null(ConditionsAssessment.Of(Hour(daylight: true), new ConditionsLimits { AvoidDarkness = true }));
    }

    // ---- which cause is named ----

    [Fact]
    public void TheWorstUnderfootIsNamedFirst()
    {
        var everything = Hour(temperature: 95, chance: 90, kind: PrecipitationKind.Ice, air: 250, daylight: false);
        var limits = new ConditionsLimits { AvoidDarkness = true };

        Assert.Equal(ConditionCause.Ice, ConditionsAssessment.Of(everything, limits));
        Assert.Equal(ConditionCause.Snow, ConditionsAssessment.Of(everything with { Precipitation = PrecipitationKind.Snow }, limits));
        Assert.Equal(ConditionCause.Rain, ConditionsAssessment.Of(everything with { Precipitation = PrecipitationKind.Rain }, limits));
        Assert.Equal(ConditionCause.Heat, ConditionsAssessment.Of(everything with { Precipitation = PrecipitationKind.None, PrecipitationChance = 0 }, limits));
        Assert.Equal(ConditionCause.PoorAir, ConditionsAssessment.Of(everything with { Precipitation = PrecipitationKind.None, PrecipitationChance = 0, TemperatureFahrenheit = 60 }, limits));
        Assert.Equal(ConditionCause.Darkness, ConditionsAssessment.Of(everything with { Precipitation = PrecipitationKind.None, PrecipitationChance = 0, TemperatureFahrenheit = 60, AirQualityIndex = 20 }, limits));
    }

    [Fact]
    public void TheLimitsCanBeMoved()
    {
        var relaxed = new ConditionsLimits { HeatFahrenheit = 100, WetChancePercent = 80, PoorAirIndex = 150 };

        Assert.Null(ConditionsAssessment.Of(Hour(temperature: 95), relaxed));
        Assert.Null(ConditionsAssessment.Of(Hour(chance: 70, kind: PrecipitationKind.Rain), relaxed));
        Assert.Null(ConditionsAssessment.Of(Hour(air: 140), relaxed));
        Assert.Equal(ConditionCause.Heat, ConditionsAssessment.Of(Hour(temperature: 100), relaxed));
    }

    [Fact]
    public void LimitsThatMakeNoSenseAreRefused()
    {
        var request = ConditionsFixtures.FreeWeek();
        var plan = ConditionsFixtures.Week();
        var read = ConditionsRead.Of([]);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsAdapter.Apply(request, plan, read, new ConditionsLimits { WetChancePercent = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsAdapter.Apply(request, plan, read, new ConditionsLimits { WetChancePercent = 101 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsAdapter.Apply(request, plan, read, new ConditionsLimits { MoveStep = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsAdapter.Apply(request, plan, read, new ConditionsLimits { ShowerRoom = TimeSpan.FromMinutes(-1) }));
    }

    // ---- a slot across the hours ----

    private static ConditionsRead Day(params (int Hour, HourConditions Value)[] bad) =>
        ConditionsRead.Of(Enumerable.Range(0, 24).Select(hour => bad.Any(item => item.Hour == hour) ? bad.First(item => item.Hour == hour).Value with { Start = At(1, hour) } : Hour() with { Start = At(1, hour) }));

    [Fact]
    public void ASlotIsBadIfAnyHourItTouchesIsBad()
    {
        var conditions = Day((18, Hour(chance: 80, kind: PrecipitationKind.Rain)));

        Assert.Equal(ConditionCause.Rain, ConditionsAssessment.Of(conditions, At(1, 17, 40), At(1, 18, 15), Limits));
        Assert.Null(ConditionsAssessment.Of(conditions, At(1, 17, 0), At(1, 17, 40), Limits));
    }

    [Fact]
    public void ASlotThatEndsWhereABadHourBeginsDoesNotTouchIt()
    {
        var conditions = Day((18, Hour(chance: 80, kind: PrecipitationKind.Rain)));

        Assert.Null(ConditionsAssessment.Of(conditions, At(1, 17, 25), At(1, 18, 0), Limits));
        Assert.Null(ConditionsAssessment.Of(conditions, At(1, 19, 0), At(1, 19, 35), Limits));
    }

    [Fact]
    public void TheFirstBadHourNamesTheCause()
    {
        var conditions = Day((17, Hour(air: 200)), (18, Hour(chance: 80, kind: PrecipitationKind.Rain)));

        Assert.Equal(ConditionCause.PoorAir, ConditionsAssessment.Of(conditions, At(1, 17, 30), At(1, 18, 30), Limits));
    }

    [Fact]
    public void HoursTheForecastDoesNotHoldAreNotBad() =>
        Assert.Null(ConditionsAssessment.Of(ConditionsRead.Of([]), At(1, 17), At(1, 18), Limits));

    [Fact]
    public void AnUnavailableForecastIsNotBadEither() =>
        Assert.Null(ConditionsAssessment.Of(ConditionsRead.Unavailable("The forecast service did not answer."), At(1, 17), At(1, 18), Limits));

    // ---- the read itself ----

    [Fact]
    public void AReadKeepsItsHoursInTimeOrder()
    {
        var read = ConditionsRead.Of([Hour() with { Start = At(1, 9) }, Hour() with { Start = At(1, 7) }]);

        Assert.Equal([At(1, 7), At(1, 9)], read.Hours.Select(hour => hour.Start).ToArray());
        Assert.True(read.IsAvailable);
        Assert.Null(read.UnavailableBecause);
    }

    [Fact]
    public void AChanceOutsideAPercentageIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsRead.Of([Hour(chance: 101)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsRead.Of([Hour(chance: -1)]));
    }

    [Fact]
    public void AnIndexOutsideTheScaleIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsRead.Of([Hour(air: 501)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConditionsRead.Of([Hour(air: -1)]));
    }

    [Fact]
    public void TwoHoursStartingTogetherAreRefused() =>
        Assert.Throws<ArgumentException>(() => ConditionsRead.Of([Hour(), Hour(chance: 90)]));

    [Fact]
    public void AnUnavailableReadHasToSayWhy()
    {
        Assert.Throws<ArgumentException>(() => ConditionsRead.Unavailable(" "));
        var read = ConditionsRead.Unavailable(" Not today. ");
        Assert.False(read.IsAvailable);
        Assert.Equal("Not today.", read.UnavailableBecause);
        Assert.Empty(read.Hours);
    }

    [Fact]
    public void OverlappingMeansSharingTime()
    {
        var read = Day();

        var hours = read.Overlapping(At(1, 17, 30), At(1, 19, 0));

        Assert.Equal([At(1, 17), At(1, 18)], hours.Select(hour => hour.Start).ToArray());
    }
}
