using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Setups;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>The run's request, joined from the setup, the checklist, the owner's moves and the clock. Pure.</summary>
public class WorkoutWeekJoinTests
{
    private static WorkoutWeekPreparation Prepare(HouseholdSetupRead setup, DateTimeOffset now, EquipmentSet? equipment = null, PlanOverrides? overrides = null) =>
        WorkoutWeekJoin.Prepare(setup, ForewordSchedule, Zone, equipment ?? EquipmentSet.Everything(), overrides ?? PlanOverrides.None, now);

    private static HouseholdSetupRead Configured() => new HouseholdSetupRead.Configured(HouseholdData.Setup() with { ZoneId = null });

    private static DateOnly WeekOf(DateTimeOffset now) =>
        Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), now)).Request.WeekStart;

    [Fact]
    public void SundayIsItsOwnWeek()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), WeekOf(Edt(10, 25, 0, 5)));
    }

    [Fact]
    public void SaturdayNightIsStillTheOldWeek()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), WeekOf(Edt(10, 31, 23, 30)));
    }

    [Fact]
    public void SundayMorningStartsTheNewWeek()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), WeekOf(Edt(11, 1, 0, 30)));
    }

    [Fact]
    public void MondayIsInsideTheSundayWeek()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), WeekOf(Edt(10, 26, 0, 5)));
    }

    [Fact]
    public void AnInstantInUtcIsReadOnTheOwnersClock()
    {
        var lateSaturday = new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero);
        var earlySunday = new DateTimeOffset(2026, 11, 1, 4, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 10, 25), WeekOf(lateSaturday));
        Assert.Equal(new DateOnly(2026, 11, 1), WeekOf(earlySunday));
    }

    [Fact]
    public void TheSpringForwardWeekKeepsItsSunday()
    {
        Assert.Equal(new DateOnly(2026, 3, 1), WeekOf(Est(3, 7, 23, 30)));
        Assert.Equal(new DateOnly(2026, 3, 8), WeekOf(Est(3, 8, 0, 30)));
        Assert.Equal(new DateOnly(2026, 3, 8), WeekOf(Edt(3, 8, 12, 30)));
    }

    [Fact]
    public void TheFallBackSundayOpensItsOwnWeek()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), WeekOf(Edt(11, 1, 1, 30)));
        Assert.Equal(new DateOnly(2026, 11, 1), WeekOf(Est(11, 1, 1, 30)));
    }

    [Fact]
    public void NoSetupPlansNothingAndSaysWhy()
    {
        var stop = Assert.IsType<WorkoutWeekPreparation.Stop>(Prepare(new HouseholdSetupRead.NotSet(PlanningSetupCodec.NothingSavedReason), Est(11, 2, 5, 30)));

        Assert.Equal(NotPlannedBecause.NotConfigured, stop.Outcome.Because);
        Assert.Equal(PlanningSetupCodec.NothingSavedReason, stop.Outcome.Detail);
        Assert.Empty(stop.Outcome.Unreadable);
    }

    [Fact]
    public void ASetupGivesTheSundayAndTheClock()
    {
        var now = Est(11, 4, 6, 45);

        var ready = Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), now));

        Assert.Equal(new DateOnly(2026, 11, 1), ready.Request.WeekStart);
        Assert.Equal(now, ready.Request.Now);
        Assert.Equal(Zone.GetUtcOffset(now), ready.Request.Now.Offset);
    }

    [Fact]
    public void TheClockIsPutOnTheHouseholdsOffset()
    {
        var utc = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        var ready = Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), utc));

        Assert.Equal(utc, ready.Request.Now);
        Assert.Equal(TimeSpan.FromHours(-4), ready.Request.Now.Offset);
    }

    [Fact]
    public void ThePeopleAndRulesAreTheSetups()
    {
        var ready = Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), Est(11, 2, 5, 30)));

        Assert.Equal([Sam, Lee], ready.Request.Rules.Adults.Select(adult => adult.Name));
        Assert.Equal(HouseholdData.Setup().DeliverFor, ready.Request.DeliverFor);
    }

    [Fact]
    public void TheTickedEquipmentAndTheOwnersMovesAreCarried()
    {
        var equipment = EquipmentSet.Of([EquipmentCatalogue.TreadmillId]);

        var ready = Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), Est(11, 2, 5, 30), equipment));

        Assert.Same(equipment, ready.Request.Equipment);
        Assert.Same(PlanOverrides.None, ready.Request.PlanOverrides);
    }

    [Fact]
    public void WhatWasAssumedIsReported()
    {
        var ready = Assert.IsType<WorkoutWeekPreparation.Ready>(Prepare(Configured(), Est(11, 2, 5, 30)));

        Assert.Contains(Constitution.Domain.Diagnostics.AssumedSetting.LunchGap, ready.Assumed);
    }
}
