using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// The household's own inputs to the busy-time model: who the adults are, when dinner is, when the
/// children's routine runs, and what a lunch gap on a WFH day looks like. Each is checked when it
/// is made, with a specific message, because a window that crosses midnight or has no length would
/// quietly protect the wrong evening.
/// </summary>
public class PlanningRulesTests
{
    // ---- family dinner ----

    [Fact]
    public void DinnerHasAStartAndALength()
    {
        var dinner = new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(75));

        Assert.Equal(new TimeOnly(18, 0), dinner.Start);
        Assert.Equal(new TimeOnly(19, 15), dinner.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void DinnerMustTakeSomeTime(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void DinnerMayNotRunPastMidnight()
    {
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => new FamilyDinner(new TimeOnly(23, 30), TimeSpan.FromMinutes(60)));

        Assert.Contains("midnight", failure.Message);
    }

    [Fact]
    public void DinnerMayEndExactlyAtMidnight()
    {
        var dinner = new FamilyDinner(new TimeOnly(23, 0), TimeSpan.FromMinutes(60));

        Assert.Equal(TimeOnly.MinValue, dinner.End);
        Assert.True(dinner.EndsAtMidnight);
    }

    [Fact]
    public void AssumedDinnerIsAnEveningHour()
    {
        var assumed = FamilyDinner.Assumed;

        Assert.Equal(new TimeOnly(17, 30), assumed.Start);
        Assert.Equal(TimeSpan.FromMinutes(60), assumed.Length);
    }

    // ---- bedtime routine ----

    [Fact]
    public void TheRoutineRunsUpToTheBedtime()
    {
        var routine = new BedtimeRoutine(new TimeOnly(20, 0), TimeSpan.FromMinutes(45));

        Assert.False(routine.IsNone);
        Assert.Equal(new TimeOnly(19, 15), routine.Begins);
        Assert.Equal(new TimeOnly(20, 0), routine.Bedtime);
    }

    [Fact]
    public void ABedtimeAloneGetsTheUsualLength()
    {
        var routine = BedtimeRoutine.At(new TimeOnly(19, 30));

        Assert.Equal(TimeSpan.FromMinutes(60), routine.Length);
        Assert.Equal(new TimeOnly(18, 30), routine.Begins);
    }

    [Fact]
    public void NoneMeansNoChildrenToPutToBed()
    {
        Assert.True(BedtimeRoutine.None.IsNone);
    }

    [Fact]
    public void ARoutineMustTakeSomeTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedtimeRoutine(new TimeOnly(20, 0), TimeSpan.Zero));
    }

    [Fact]
    public void ARoutineMayNotBeginTheEveningBefore()
    {
        // A bedtime of 12:30 am with a 60-minute routine would start at 11:30 pm the day before. The
        // setting is an evening time; a morning one is refused rather than protecting the wrong night.
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => new BedtimeRoutine(new TimeOnly(0, 30), TimeSpan.FromMinutes(60)));

        Assert.Contains("evening", failure.Message);
    }

    [Fact]
    public void TheAssumedBedtimeIsAnEvening()
    {
        Assert.Equal(new TimeOnly(19, 30), BedtimeRoutine.Assumed.Bedtime);
    }

    // ---- WFH lunch ----

    [Fact]
    public void ALunchGapHasAWindowAndWords()
    {
        var lunch = new WorkFromHomeLunch(new TimeOnly(12, 0), TimeSpan.FromMinutes(45), ["WFH"]);

        Assert.Equal(new TimeOnly(12, 45), lunch.End);
        Assert.Equal(["WFH"], lunch.Keywords);
    }

    [Fact]
    public void ALunchGapMustTakeSomeTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkFromHomeLunch(new TimeOnly(12, 0), TimeSpan.Zero, ["WFH"]));
    }

    [Fact]
    public void ALunchGapMayNotRunPastMidnight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkFromHomeLunch(new TimeOnly(23, 30), TimeSpan.FromMinutes(60), ["WFH"]));
    }

    [Fact]
    public void NoLunchWordsNoWfhDay()
    {
        Assert.Empty(WorkFromHomeLunch.None.Keywords);
    }

    [Fact]
    public void BlankLunchWordsAreDropped()
    {
        var lunch = new WorkFromHomeLunch(new TimeOnly(12, 0), TimeSpan.FromMinutes(60), ["WFH", " ", ""]);

        Assert.Equal(["WFH"], lunch.Keywords);
    }

    // ---- adults ----

    [Fact]
    public void AnAdultNeedsAName()
    {
        Assert.Throws<ArgumentException>(() => new Adult("  ", true));
    }

    [Fact]
    public void AnAdultKeepsTheirAddressesAndOtherNames()
    {
        var adult = new Adult(Sam, false, [SamAddress], ["Dad"]);

        Assert.False(adult.OnPickup);
        Assert.Equal([SamAddress], adult.Addresses);
        Assert.Equal(["Dad"], adult.OtherNames);
    }

    [Fact]
    public void AnAdultWithNoAddressesStillWorks()
    {
        var adult = new Adult(Sam, true);

        Assert.Empty(adult.Addresses);
        Assert.Empty(adult.OtherNames);
    }
}
