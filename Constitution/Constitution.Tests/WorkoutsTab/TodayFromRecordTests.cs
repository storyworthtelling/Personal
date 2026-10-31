using Constitution.Domain.Today;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>Today's clipping from the saved week, read on the owner's clock: the day changes at their midnight, not the phone's or the server's.</summary>
public sealed class TodayFromRecordTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly DateOnly Tuesday = new(2026, 11, 3);

    private static WorkoutWeekRecordRead Held(IReadOnlyList<StoredToday>? today = null, IReadOnlyList<StoredSession>? sessions = null) =>
        new WorkoutWeekRecordRead.Held(new StoredWorkoutWeek(
            new DateOnly(2026, 11, 2),
            Est(11, 2, 5, 30),
            sessions ?? [new StoredSession(Sam, Tuesday, Est(11, 3, 5, 30), "Strength", 40, false, [Sam], "None", ["Squat"], 132)],
            [],
            [],
            today ?? [new StoredToday(Sam, Tuesday, "Strength", new TimeOnly(5, 30), 40, 132)]));

    private static TodayClipping? Clip(TodayPlan? plan) => TodayModel.Clipping(plan);

    [Fact]
    public void TheClippingSaysWhatTheRunFound()
    {
        var plan = TodayPlanFromRecord.For(Held(), Sam, Est(11, 3, 7), Eastern);

        var clipping = Clip(plan)!;
        Assert.Equal("Workout · 5:30", clipping.Kicker);
        Assert.Equal("Strength, 40 minutes", clipping.Headline);
        Assert.Equal("Score 132 bpm · Steady", clipping.ScoreLine);
    }

    [Fact]
    public void AnOutdoorWalkIsInWordsAndNotInTheFilesSpelling()
    {
        var sessions = new[] { new StoredSession(Sam, Tuesday, Est(11, 3, 7), "OutdoorCardio", 30, false, [Sam], "None", ["Walk"], 118) };
        var today = new[] { new StoredToday(Sam, Tuesday, "OutdoorCardio", new TimeOnly(7, 0), 30, 118) };

        var clipping = Clip(TodayPlanFromRecord.For(Held(today, sessions), Sam, Est(11, 3, 7), Eastern))!;

        Assert.Equal("Outdoor walk, 30 minutes", clipping.Headline);
    }

    [Fact]
    public void ARestDayHasNoClipping()
    {
        var today = new[] { new StoredToday(Sam, Tuesday, null, null, null, null) };

        Assert.Null(TodayPlanFromRecord.For(Held(today, []), Sam, Est(11, 3, 7), Eastern));
    }

    [Fact]
    public void NoReportAndAnUnreadableOneBothClipNothing()
    {
        Assert.Null(TodayPlanFromRecord.For(new WorkoutWeekRecordRead.NothingYet(), Sam, Est(11, 3, 7), Eastern));
        Assert.Null(TodayPlanFromRecord.For(new WorkoutWeekRecordRead.Unreadable("The saved week could not be read."), Sam, Est(11, 3, 7), Eastern));
    }

    [Fact]
    public void ThePersonIsTheOwnersAlone()
    {
        var today = new[] { new StoredToday(Lee, Tuesday, "Strength", new TimeOnly(5, 30), 40, 132) };

        Assert.Null(TodayPlanFromRecord.For(Held(today, []), Sam, Est(11, 3, 7), Eastern));
    }

    [Fact]
    public void AnotherDayInTheWeekReadsThatDaysSession()
    {
        var wednesday = new DateOnly(2026, 11, 4);
        var sessions = new[] { new StoredSession(Sam, wednesday, Est(11, 4, 18), "Mobility", 20, false, [Sam], "None", ["Roll"], 118) };

        var clipping = Clip(TodayPlanFromRecord.For(Held(sessions: sessions), Sam, Est(11, 4, 9), Eastern))!;

        Assert.Equal("Workout · 6:00", clipping.Kicker);
        Assert.Equal("Mobility, 20 minutes", clipping.Headline);
    }

    [Fact]
    public void ADayAfterTheWeekHasNoClipping()
    {
        Assert.Null(TodayPlanFromRecord.For(Held(), Sam, Est(11, 9, 7), Eastern));
    }

    [Fact]
    public void JustBeforeMidnightOnTheirClockIsStillThatDay()
    {
        // 23:50 Tuesday in New York is already Wednesday in UTC; the owner is still on Tuesday.
        var late = new DateTimeOffset(2026, 11, 4, 4, 50, 0, TimeSpan.Zero);

        Assert.NotNull(Clip(TodayPlanFromRecord.For(Held(), Sam, late, Eastern)));
    }

    [Fact]
    public void JustAfterMidnightOnTheirClockIsTheNextDay()
    {
        // 00:10 Wednesday in New York is 05:10 UTC Wednesday, and 23:10 Tuesday on a clock five hours behind that.
        var early = new DateTimeOffset(2026, 11, 4, 5, 10, 0, TimeSpan.Zero);

        Assert.Null(Clip(TodayPlanFromRecord.For(Held(), Sam, early, Eastern)));
    }

    [Fact]
    public void AFamilyWalkIsCalledOneWhenTheSessionIsShared()
    {
        var sessions = new[] { new StoredSession(Sam, Tuesday, Est(11, 3, 9), "GentleWalk", 45, false, [Sam, Lee], "FamilyWalk", ["Walk"], 118) };
        var today = new[] { new StoredToday(Sam, Tuesday, "GentleWalk", new TimeOnly(9, 0), 45, 118) };

        var clipping = Clip(TodayPlanFromRecord.For(Held(today, sessions), Sam, Est(11, 3, 7), Eastern))!;

        Assert.Equal("Family walk, 45 minutes", clipping.Headline);
    }

    [Fact]
    public void AKindThisAppNeverWroteClipsNothingRatherThanGuess()
    {
        var today = new[] { new StoredToday(Sam, Tuesday, "Zumba", new TimeOnly(9, 0), 45, 118) };

        Assert.Null(TodayPlanFromRecord.For(Held(today, []), Sam, Est(11, 3, 7), Eastern));
    }
}
