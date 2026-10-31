using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// B2: the key a session wears is worked out from what was issued, not from where it sits among its kind this run. The
/// week is re-planned from its first instant on every run, so "the second strength session" is a different session after
/// a Monday meeting drops the first; "the strength session on a Wednesday" is not.
/// </summary>
public class SessionKeysTests
{
    private static PlannedSession Strength(int dayOffset, params string[] who) => Planned(dayOffset, (16, 0), (18, 0), Effort(40), who.Length == 0 ? [Sam] : who);

    private static PlannedSession Mobility(int dayOffset) => Planned(dayOffset, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam);

    private static PlacementKey Key(int ordinal, string person = Sam, SessionKind kind = SessionKind.Strength) => PlacementKey.ForWorkout(person, kind, Week, ordinal);

    // ---- a first week ----

    [Fact]
    public void AFirstWeekIsNumberedInTimeOrder()
    {
        var plan = PlanOf(Strength(4), Strength(0), Strength(2));

        var keys = SessionKeys.For(plan, KeyAssignments.Empty);

        Assert.Equal(Key(0), keys.KeyOn(Sam, Day(0)));
        Assert.Equal(Key(1), keys.KeyOn(Sam, Day(2)));
        Assert.Equal(Key(2), keys.KeyOn(Sam, Day(4)));
        Assert.Equal(3, keys.Issued.Count);
    }

    [Fact]
    public void NoRecordGivesTheSameNumbersAsAnEmptyOne()
    {
        var plan = PlanOf(Strength(0), Strength(2));

        Assert.Equal(SessionKeys.For(plan, KeyAssignments.Empty).Issued, SessionKeys.For(plan, null).Issued);
    }

    [Fact]
    public void KindsAreCountedApart()
    {
        var plan = PlanOf(Strength(0), Mobility(1), Strength(2), Mobility(3));

        var keys = SessionKeys.For(plan, KeyAssignments.Empty);

        Assert.Equal(Key(0), keys.KeyOn(Sam, Day(0)));
        Assert.Equal(Key(0, kind: SessionKind.Mobility), keys.KeyOn(Sam, Day(1)));
        Assert.Equal(Key(1), keys.KeyOn(Sam, Day(2)));
        Assert.Equal(Key(1, kind: SessionKind.Mobility), keys.KeyOn(Sam, Day(3)));
    }

    [Fact]
    public void TheKeysAreTheSpellingTheCalendarAlreadyHolds()
    {
        var keys = SessionKeys.For(PlanOf(Strength(0), Strength(2)), KeyAssignments.Empty);

        Assert.Equal($"c1:sam:strength:{Week}:1", keys.KeyOn(Sam, Day(2))!.Value.Value);
    }

    // ---- a later week ----

    [Fact]
    public void ADroppedDayDoesNotRenumberTheOthers()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty);

        var later = SessionKeys.For(PlanOf(Strength(2), Strength(4)), first.Assignments);

        Assert.Equal(Key(1), later.KeyOn(Sam, Day(2)));
        Assert.Equal(Key(2), later.KeyOn(Sam, Day(4)));
        Assert.Empty(later.Issued);
    }

    [Fact]
    public void ANewDayTakesANumberAboveEveryoneIssued()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty);

        var later = SessionKeys.For(PlanOf(Strength(1), Strength(2), Strength(4)), first.Assignments);

        Assert.Equal(Key(3), later.KeyOn(Sam, Day(1)));
        Assert.Equal(Key(1), later.KeyOn(Sam, Day(2)));
        Assert.Equal(Key(3), Assert.Single(later.Issued).Key);
    }

    [Fact]
    public void ARetiredNumberIsNeverGivenToAnotherSession()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty);
        var dropped = SessionKeys.For(PlanOf(Strength(4)), first.Assignments);

        var added = SessionKeys.For(PlanOf(Strength(3), Strength(4)), dropped.Assignments);

        Assert.Equal(Key(3), added.KeyOn(Sam, Day(3)));
        Assert.Equal(Key(2), added.KeyOn(Sam, Day(4)));
    }

    [Fact]
    public void ADayThatCameBackWearsItsOldKey()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2)), KeyAssignments.Empty);
        var dropped = SessionKeys.For(PlanOf(Strength(2)), first.Assignments);

        var back = SessionKeys.For(PlanOf(Strength(0), Strength(2)), dropped.Assignments);

        Assert.Equal(Key(0), back.KeyOn(Sam, Day(0)));
        Assert.Empty(back.Issued);
    }

    [Fact]
    public void ANewKindStartsFromNothing()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2)), KeyAssignments.Empty);

        var later = SessionKeys.For(PlanOf(Strength(0), Mobility(1), Strength(2)), first.Assignments);

        Assert.Equal(Key(0, kind: SessionKind.Mobility), later.KeyOn(Sam, Day(1)));
    }

    [Fact]
    public void WhatWasIssuedIsKeptInTheRecordItHandsBack()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2)), KeyAssignments.Empty);

        var later = SessionKeys.For(PlanOf(Strength(1), Strength(2)), first.Assignments);

        Assert.True(later.Assignments.Vouches(Key(0)));
        Assert.True(later.Assignments.Vouches(Key(1)));
        Assert.True(later.Assignments.Vouches(Key(2)));
        Assert.Equal(2, first.Assignments.Issues.Count);
    }

    [Fact]
    public void AnotherWeekIsNumberedAlone()
    {
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty);
        var nextMonday = WeekStart.AddDays(7);
        var next = new HouseholdWorkoutPlan(
            nextMonday,
            [new WeeklyPlan(Sam, nextMonday, [Strength(0) with { Day = nextMonday, Start = Strength(0).Start.AddDays(7), Gap = new(Strength(0).Gap.Start.AddDays(7), Strength(0).Gap.End.AddDays(7)) }], null)],
            []);

        var later = SessionKeys.For(next, first.Assignments);

        Assert.Equal(PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week + 1, 0), later.KeyOn(Sam, nextMonday));
    }

    // ---- who and what ----

    [Fact]
    public void TwoSessionsOfOneKindOnOneDayAreTwoKeys()
    {
        var morning = Planned(0, (9, 0), (10, 0), Effort(20), Sam);
        var evening = Planned(0, (16, 0), (18, 0), Effort(40), Sam);
        var first = SessionKeys.For(PlanOf(evening, morning), KeyAssignments.Empty);

        var again = SessionKeys.For(PlanOf(morning, evening), first.Assignments);

        Assert.Equal(Key(0), first.KeyOf(Sam, morning));
        Assert.Equal(Key(1), first.KeyOf(Sam, evening));
        Assert.Equal(first.KeyOf(Sam, morning), again.KeyOf(Sam, morning));
        Assert.Equal(first.KeyOf(Sam, evening), again.KeyOf(Sam, evening));
        Assert.Empty(again.Issued);
    }

    [Fact]
    public void ASharedSessionHasAKeyForEachPerson()
    {
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);
        var plan = PlanOf(Strength(0), Strength(1, Lee), walk);

        var keys = SessionKeys.For(plan, KeyAssignments.Empty);

        Assert.Equal(Key(0, Sam, SessionKind.GentleWalk), keys.KeyOf(Sam, walk));
        Assert.Equal(Key(0, Lee, SessionKind.GentleWalk), keys.KeyOf(Lee, walk));
        Assert.Equal(Key(0), keys.KeyOn(Sam, Day(0)));
        Assert.Equal(Key(0, Lee), keys.KeyOn(Lee, Day(1)));
    }

    [Fact]
    public void ASharedSessionOnlyInTheSharedListStillGetsKeys()
    {
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);
        var plan = PlanOf(walk) with { Plans = [new WeeklyPlan(Sam, WeekStart, [], null), new WeeklyPlan(Lee, WeekStart, [], null)] };

        var keys = SessionKeys.For(plan, KeyAssignments.Empty);

        Assert.NotNull(keys.KeyOf(Sam, walk));
        Assert.NotNull(keys.KeyOf(Lee, walk));
    }

    [Fact]
    public void APersonIsMatchedWithoutCase()
    {
        var keys = SessionKeys.For(PlanOf(Strength(0)), KeyAssignments.Empty);

        Assert.Equal(Key(0), keys.KeyOn("SAM", Day(0)));
        Assert.Equal(Key(0), keys.KeyOn(" sam ", Day(0)));
    }

    [Fact]
    public void ARestDayHasNoKey()
    {
        var keys = SessionKeys.For(PlanOf(Strength(0)), KeyAssignments.Empty);

        Assert.Null(keys.KeyOn(Sam, Day(1)));
        Assert.Null(keys.KeyOn(Lee, Day(0)));
        Assert.Null(keys.KeyOf(Lee, Strength(0)));
    }

    [Fact]
    public void TheKeyOnADayIsTheEarlierSessionsWhenTheresTwo()
    {
        var morning = Planned(0, (9, 0), (10, 0), Effort(20), Sam);
        var evening = Planned(0, (16, 0), (18, 0), Effort(40), Sam);

        var keys = SessionKeys.For(PlanOf(evening, morning), KeyAssignments.Empty);

        Assert.Equal(keys.KeyOf(Sam, morning), keys.KeyOn(Sam, Day(0)));
    }

    [Fact]
    public void AMissingPlanIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => SessionKeys.For(null!, KeyAssignments.Empty));
    }
}
