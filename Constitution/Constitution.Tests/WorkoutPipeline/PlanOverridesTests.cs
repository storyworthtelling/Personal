using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The owner's swaps and moves, kept so the weekly run plans the week they left. The strongest thing to hold is that applying
/// what the tab recorded to the plan the run derives gives back exactly the plan the tab showed after the change, so the
/// calendar keys the tab wrote and the keys the run works out cannot disagree.
/// </summary>
public class PlanOverridesTests
{
    private static WorkoutsSnapshot Start() => Snapshot();

    private static SwapResult Made(WorkoutsSnapshot snapshot, DateOnly from, DateOnly to) =>
        SwapChoices.Check(snapshot, from, to).Result ?? throw new InvalidOperationException($"{from} to {to} was not allowed.");

    private static PlanOverrides Recorded(SwapResult swap) => PlanOverrides.From(new WorkoutsChange.Swapped(swap));

    /// <summary>The person's sessions as comparable text, so two plans can be held to each other without leaning on object identity.</summary>
    private static List<string> Shape(HouseholdWorkoutPlan plan, string person = Sam) =>
        plan.PlanFor(person)!.Sessions
            .Select(item => $"{item.Day:yyyy-MM-dd} {item.Start:O} {item.Gap.Start:O}-{item.Gap.End:O} {item.Session.Kind} {item.Session.Duration} {string.Join('+', item.Session.Exercises.Select(e => e.Name))} {string.Join(',', item.Participants)} {item.Together}")
            .ToList();

    private static HouseholdWorkoutPlan BaseWeek(WorkoutsSnapshot snapshot, int week) => snapshot.Weeks[week].Plan;

    [Fact]
    public void NoOverridesLeaveThePlanItself()
    {
        var plan = BaseWeek(Start(), 0);

        var applied = PlanOverrides.None.ApplyTo(plan);

        Assert.Same(plan, applied.Plan);
        Assert.Empty(applied.Relocated);
        Assert.Empty(applied.Unresolved);
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(4, 6)]
    [InlineData(5, 7)]
    [InlineData(7, 3)]
    [InlineData(8, 6)]
    public void WhatTheTabShowedIsWhatTheRunPlans(int fromDay, int toDay)
    {
        var snapshot = Start();
        var from = new DateOnly(2026, 11, fromDay);
        var to = new DateOnly(2026, 11, toDay);
        var swap = Made(snapshot, from, to);
        var shown = SwapChoices.Apply(snapshot, swap);

        var overrides = Recorded(swap);

        Assert.Equal(Shape(shown.Weeks[0].Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 0)).Plan));
        Assert.Equal(Shape(shown.Weeks[1].Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 1)).Plan));
    }

    [Fact]
    public void ASwapAcrossTheWeekEndIsMadeInBothWeeks()
    {
        var snapshot = Start();
        var swap = Made(snapshot, Sat, Mon);
        var shown = SwapChoices.Apply(snapshot, swap);

        var first = Recorded(swap).ApplyTo(BaseWeek(snapshot, 0)).Plan;
        var second = Recorded(swap).ApplyTo(BaseWeek(snapshot, 1)).Plan;

        Assert.DoesNotContain(first.PlanFor(Sam)!.Sessions, item => item.Session.Kind == SessionKind.Treadmill);
        Assert.Contains(second.PlanFor(Sam)!.Sessions, item => item.Day == Mon && item.Session.Kind == SessionKind.Treadmill);
        Assert.Equal(Shape(shown.Weeks[0].Plan), Shape(first));
        Assert.Equal(Shape(shown.Weeks[1].Plan), Shape(second));
    }

    [Fact]
    public void TwoSessionsOfOneKindSwapAsOneChange()
    {
        var snapshot = Start();
        var swap = Made(snapshot, Tue, Thu);
        var shown = SwapChoices.Apply(snapshot, swap);

        var applied = Recorded(swap).ApplyTo(BaseWeek(snapshot, 0));

        Assert.Equal(2, applied.Relocated.Count);
        var keys = WorkoutKeys.Of(applied.Plan, Sam).Values.Select(key => key.Value).Order().ToList();
        Assert.Equal(WorkoutKeys.Of(shown.Weeks[0], Sam).Values.Select(key => key.Value).Order(), keys);
    }

    [Fact]
    public void ASecondChangeSitsOnTheFirst()
    {
        var snapshot = Start();
        var first = Made(snapshot, Tue, Thu);
        var afterFirst = SwapChoices.Apply(snapshot, first);
        var second = Made(afterFirst, Wed, Fri);
        var shown = SwapChoices.Apply(afterFirst, second);

        var overrides = Recorded(first).Then(Recorded(second));

        Assert.Equal(Shape(shown.Weeks[0].Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 0)).Plan));
    }

    [Fact]
    public void ALaterChangeIsMadeAfterTheFirst()
    {
        var snapshot = Start();
        var first = Made(snapshot, Thu, Sat);
        var afterFirst = SwapChoices.Apply(snapshot, first);
        var second = Made(afterFirst, Thu, Mon);
        var shown = SwapChoices.Apply(afterFirst, second);

        var overrides = Recorded(first).Then(Recorded(second));

        Assert.Equal(Shape(shown.Weeks[0].Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 0)).Plan));
        Assert.Equal(Shape(shown.Weeks[1].Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 1)).Plan));
    }

    [Fact]
    public void AStaleRecordChangesNothing()
    {
        var snapshot = Start();
        var swap = Made(snapshot, Wed, Fri);
        var overrides = Recorded(swap);
        var moved = overrides.Groups[0].Moves[0];
        var stale = new PlanOverrides([new MoveGroup([moved with { Key = "c1:sam:mobility:99:7" }])], []);

        var applied = stale.ApplyTo(BaseWeek(snapshot, 0));

        Assert.Same(BaseWeek(snapshot, 0), applied.Plan);
        Assert.Single(applied.Unresolved);
    }

    [Fact]
    public void AMoveTouchesOnlyItsOwnPerson()
    {
        var snapshot = Start();
        var lee = new WeeklyPlan(Lee, BaseWeek(snapshot, 0).WeekStart, [WedMobility() with { Participants = [Lee] }], null);
        var week = BaseWeek(snapshot, 0);
        var both = week with { Plans = [.. week.Plans, lee] };

        var applied = Recorded(Made(snapshot, Wed, Fri)).ApplyTo(both);

        Assert.Equal(Shape(both, Lee), Shape(applied.Plan, Lee));
        Assert.NotEqual(Shape(both), Shape(applied.Plan));
    }

    [Fact]
    public void TheSessionKeepsItsExercisesAndWhoIsInIt()
    {
        var snapshot = Start();
        var original = snapshot.SessionOn(Wed)!;

        var applied = Recorded(Made(snapshot, Wed, Fri)).ApplyTo(BaseWeek(snapshot, 0));

        var moved = applied.Plan.PlanFor(Sam)!.SessionsOn(Fri).Single();
        Assert.Equal(original.Session.Exercises.Select(item => item.Name), moved.Session.Exercises.Select(item => item.Name));
        Assert.Equal(original.Participants, moved.Participants);
        Assert.Equal(Together.None, moved.Together);
    }

    [Fact]
    public void ARelocationNamesTheSessionBeforeAndAfter()
    {
        var snapshot = Start();

        var applied = Recorded(Made(snapshot, Wed, Fri)).ApplyTo(BaseWeek(snapshot, 0));

        var relocation = Assert.Single(applied.Relocated);
        Assert.Equal(Wed, relocation.Old.Day);
        Assert.Equal(Fri, relocation.New.Day);
    }

    [Fact]
    public void ASkipIsRememberedByPersonAndDay()
    {
        var snapshot = Start();
        var session = snapshot.SessionOn(Thu)!;
        var skip = new PendingSkip(Sam, Thu, SessionKind.Strength, session, null, null, Now);

        var overrides = PlanOverrides.From(new WorkoutsChange.SkipCommitted(skip));

        Assert.True(overrides.IsSkipped(Sam, Thu));
        Assert.True(overrides.IsSkipped(" sam ", Thu));
        Assert.False(overrides.IsSkipped(Sam, Wed));
        Assert.False(overrides.IsSkipped(Lee, Thu));
        Assert.Empty(overrides.Groups);
    }

    [Fact]
    public void ASkipLeavesThePlanAsItIs()
    {
        var snapshot = Start();
        var skip = new PendingSkip(Sam, Thu, SessionKind.Strength, snapshot.SessionOn(Thu)!, null, null, Now);
        var overrides = PlanOverrides.From(new WorkoutsChange.SkipCommitted(skip));

        var applied = overrides.ApplyTo(BaseWeek(snapshot, 0));

        Assert.Same(BaseWeek(snapshot, 0), applied.Plan);
    }

    [Fact]
    public void ASwapIsRecordedUnderTheTabsKeys()
    {
        var snapshot = Start();
        var swap = Made(snapshot, Thu, Sat);

        var group = Assert.Single(Recorded(swap).Groups);

        Assert.Equal(2, group.Moves.Count);
        foreach (var move in swap.Moves)
        {
            var recorded = group.Moves.Single(item => item.FromDay == move.Old.Day);
            Assert.Equal(WorkoutKeys.In(swap.Before, Sam, move.Old).Value, recorded.Key);
            Assert.Equal(move.New.Day, recorded.Day);
            Assert.Equal(move.New.Start, recorded.Start);
            Assert.Equal(move.New.Gap, recorded.Gap);
            Assert.Equal(Sam, recorded.Person);
        }
    }

    [Fact]
    public void TheKeyNeverHoldsADate()
    {
        var snapshot = Start();

        var group = Assert.Single(Recorded(Made(snapshot, Thu, Sat)).Groups);

        Assert.All(group.Moves, move => Assert.DoesNotContain("2026", move.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyingTwiceGivesTheSamePlan()
    {
        var snapshot = Start();
        var overrides = Recorded(Made(snapshot, Thu, Sat));

        Assert.Equal(Shape(overrides.ApplyTo(BaseWeek(snapshot, 0)).Plan), Shape(overrides.ApplyTo(BaseWeek(snapshot, 0)).Plan));
    }
}
