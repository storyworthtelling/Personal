using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.WorkoutsTab;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// B2 for the owner's moves: a move names its session by the key it wore, and the run finds it again in a week it has re-planned.
/// Named by place among its kind, "the second strength session" is Wednesday's until a Monday meeting drops the first, and
/// then it is Friday's, so the run moved the wrong session and said nothing.
/// </summary>
public class StableOverridesTests
{
    private static PlannedSession Strength(int day) => Planned(day, (16, 0), (18, 0), Effort(40), Sam);

    private static KeyAssignments TheFirstWeek() =>
        SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty).Assignments;

    private static PlacementKey Key(int ordinal) => StrengthKey(Sam, ordinal);

    [Fact]
    public void AMoveIsRecordedUnderTheNumberItsSessionWore()
    {
        var snapshot = WorkoutScreenFixture.Snapshot() with
        {
            Assignments = KeyAssignments.Empty.With(
            [
                new KeyIssue(Sam, PlacementKey.WeekIndexOf(WorkoutScreenFixture.Tue), SessionKind.Strength, DayOfWeek.Thursday, 0, 5),
            ]),
        };
        var swap = SwapChoices.Check(snapshot, WorkoutScreenFixture.Thu, WorkoutScreenFixture.Sat).Result!;

        var moved = PlanOverrides.From(new WorkoutsChange.Swapped(swap)).Groups.Single().Moves.Single(move => move.FromDay == WorkoutScreenFixture.Thu);

        Assert.Equal(PlacementKey.ForWorkout(Sam, SessionKind.Strength, PlacementKey.WeekIndexOf(WorkoutScreenFixture.Tue), 5).Value, moved.Key);
    }

    [Fact]
    public void AMoveFindsItsSessionAfterTheWeekIsReplanned()
    {
        var record = TheFirstWeek();
        var replanned = PlanOf(Strength(2), Strength(4));
        var wednesday = replanned.Plans[0].Sessions.Single(session => session.Day == Day(2));
        var thursday = Planned(3, (16, 0), (18, 0), wednesday.Session, Sam);
        var move = new SessionMoved(Sam, Key(1).Value, Day(2), Day(3), thursday.Start, thursday.Gap, wednesday.Session);

        var applied = new PlanOverrides([new MoveGroup([move])], []).ApplyTo(replanned, record);

        Assert.Empty(applied.Unresolved);
        Assert.Same(wednesday, Assert.Single(applied.Relocated).Old);
        Assert.Equal([Day(3), Day(4)], applied.Plan.PlanFor(Sam)!.Sessions.Select(session => session.Day));
    }

    [Fact]
    public void WithNoRecordAMoveIsFoundByTheOldCount()
    {
        var plan = PlanOf(Strength(2), Strength(4));
        var friday = plan.Plans[0].Sessions.Single(session => session.Day == Day(4));
        var move = new SessionMoved(Sam, Key(1).Value, Day(4), Day(5), friday.Start, friday.Gap, friday.Session);

        var applied = new PlanOverrides([new MoveGroup([move])], []).ApplyTo(plan);

        Assert.Same(friday, Assert.Single(applied.Relocated).Old);
    }
}
