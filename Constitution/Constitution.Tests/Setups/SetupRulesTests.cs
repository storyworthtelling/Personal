using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Setups;

/// <summary>
/// The rules the weekly run plans by, made from what the household gave. Every setting it did not give takes the stated stand-in
/// (<c>FamilyDinner.Assumed</c> and the like) and is named in <see cref="SetupRules.Assumed"/>, so Diagnostics can say so: an assumption
/// the owner cannot see is a setting the owner cannot correct.
/// </summary>
public class SetupRulesTests
{
    private static SetupRules Made(PlanningSetup? setup = null, string? work = ForewordSchedule) =>
        SetupRules.From(setup ?? HouseholdData.Setup(), work, Zone);

    [Fact]
    public void AGivenHouseholdAssumesOnlyTheLunchGap()
    {
        var made = Made();

        Assert.Equal([AssumedSetting.LunchGap], made.Assumed);
    }

    [Fact]
    public void TheAdultsComeAcrossWithWhatTheyWereGiven()
    {
        var rules = Made().Rules;

        Assert.Equal(["Sam", "Lee"], rules.Adults.Select(adult => adult.Name));
        Assert.True(rules.FindAdult("Sam")!.OnPickup);
        Assert.False(rules.FindAdult("Lee")!.OnPickup);
        Assert.Equal(["sam@example.com"], rules.FindAdult("Sam")!.Addresses);
        Assert.Equal(["Dad"], rules.FindAdult("Sam")!.OtherNames);
    }

    [Fact]
    public void ADinnerAndABedtimeAreTheOnesGiven()
    {
        var rules = Made().Rules;

        Assert.Equal(new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(45)), rules.Dinner);
        Assert.Equal(BedtimeRoutine.At(new TimeOnly(20, 0)), rules.Bedtime);
    }

    [Fact]
    public void ADinnerNotGivenIsAssumed()
    {
        var made = Made(HouseholdData.Setup() with { DinnerStart = null, DinnerLength = null });

        Assert.Equal(FamilyDinner.Assumed, made.Rules.Dinner);
        Assert.Contains(AssumedSetting.DinnerTime, made.Assumed);
    }

    [Fact]
    public void ADinnerWithNoLengthTakesTheUsualHour()
    {
        var made = Made(HouseholdData.Setup() with { DinnerStart = new TimeOnly(17, 0), DinnerLength = null });

        Assert.Equal(new FamilyDinner(new TimeOnly(17, 0), TimeSpan.FromMinutes(60)), made.Rules.Dinner);
        Assert.DoesNotContain(AssumedSetting.DinnerTime, made.Assumed);
    }

    [Fact]
    public void ADinnerThatRunsPastMidnightIsAssumed()
    {
        var made = Made(HouseholdData.Setup() with { DinnerStart = new TimeOnly(23, 30), DinnerLength = TimeSpan.FromMinutes(120) });

        Assert.Equal(FamilyDinner.Assumed, made.Rules.Dinner);
        Assert.Contains(AssumedSetting.DinnerTime, made.Assumed);
    }

    [Fact]
    public void ABedtimeNotGivenIsAssumed()
    {
        var made = Made(HouseholdData.Setup() with { Bedtime = null });

        Assert.Equal(BedtimeRoutine.Assumed, made.Rules.Bedtime);
        Assert.Contains(AssumedSetting.BedtimeRoutine, made.Assumed);
    }

    [Fact]
    public void ABedtimeTooEarlyForARoutineIsAssumed()
    {
        var made = Made(HouseholdData.Setup() with { Bedtime = new TimeOnly(0, 30) });

        Assert.Equal(BedtimeRoutine.Assumed, made.Rules.Bedtime);
        Assert.Contains(AssumedSetting.BedtimeRoutine, made.Assumed);
    }

    [Fact]
    public void AHouseholdWithNoChildrenHasNoRoutineAndAssumesNone()
    {
        var made = Made(HouseholdData.Setup() with { Bedtime = null, NoChildren = true, ChildrenCalendarIds = [] });

        Assert.True(made.Rules.Bedtime.IsNone);
        Assert.DoesNotContain(AssumedSetting.BedtimeRoutine, made.Assumed);
    }

    [Fact]
    public void PickupNotSaidIsYesAndAssumed()
    {
        var setup = new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com"), new HouseholdAdult("Lee", "lee@example.com", false)])
        {
            DinnerStart = new TimeOnly(18, 0),
            Bedtime = new TimeOnly(20, 0),
        };
        var made = Made(setup);

        Assert.True(made.Rules.FindAdult("Sam")!.OnPickup);
        Assert.False(made.Rules.FindAdult("Lee")!.OnPickup);
        Assert.Contains(AssumedSetting.PickupDuty, made.Assumed);
    }

    [Fact]
    public void PickupSaidForEveryoneAssumesNothingAboutIt()
    {
        Assert.DoesNotContain(AssumedSetting.PickupDuty, Made().Assumed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"soon\",\"End\":\"16:00\"}]}")]
    public void AWorkScheduleThatCannotBeReadAssumesTheHours(string? json)
    {
        var made = Made(work: json);

        Assert.False(made.Rules.Work.IsKnown);
        Assert.Contains(AssumedSetting.WorkHours, made.Assumed);
    }

    [Fact]
    public void AKnownWorkScheduleAssumesNothing()
    {
        var made = Made();

        Assert.True(made.Rules.Work.IsKnown);
        Assert.DoesNotContain(AssumedSetting.WorkHours, made.Assumed);
    }

    [Fact]
    public void TheZoneGivenIsTheZoneUsed()
    {
        var made = Made(HouseholdData.Setup() with { ZoneId = "UTC" });

        Assert.Equal(TimeZoneInfo.Utc, made.Rules.Zone);
        Assert.DoesNotContain(AssumedSetting.TimeZone, made.Assumed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void AZoneNotGivenOrNotKnownIsThePhonesAndAssumed(string? id)
    {
        var made = Made(HouseholdData.Setup() with { ZoneId = id });

        Assert.Equal(Zone, made.Rules.Zone);
        Assert.Contains(AssumedSetting.TimeZone, made.Assumed);
    }

    [Fact]
    public void WhatIsAssumedIsListedOnceInTheOrderOfTheList()
    {
        var made = Made(new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com")]), work: null);

        Assert.Equal(made.Assumed.Distinct(), made.Assumed);
        Assert.Equal(made.Assumed.Order(), made.Assumed);
        Assert.Equal(
            [AssumedSetting.DinnerTime, AssumedSetting.BedtimeRoutine, AssumedSetting.LunchGap, AssumedSetting.PickupDuty, AssumedSetting.WorkHours, AssumedSetting.TimeZone],
            made.Assumed);
    }

    [Fact]
    public void TheLunchGapIsTheStatedStandIn()
    {
        var lunch = Made().Rules.Lunch;

        Assert.Equal(WorkFromHomeLunch.Assumed.Start, lunch.Start);
        Assert.Equal(WorkFromHomeLunch.Assumed.Length, lunch.Length);
        Assert.Equal(WorkFromHomeLunch.Assumed.Keywords, lunch.Keywords);
    }
}
