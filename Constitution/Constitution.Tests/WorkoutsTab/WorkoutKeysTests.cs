using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>The keys a swap or skip looks events up by must be the keys placement wrote them under.</summary>
public class WorkoutKeysTests
{
    [Fact]
    public void TheyAreTheKeysPlacementWanted()
    {
        var snapshot = Snapshot();
        var week = snapshot.Weeks[0];
        var request = new PlacementRequest(week.Plan.WeekStart, [WorkoutFixture.BusyFor(Sam, Calendars())], [], week.Plan);

        var wanted = PlacementEngine.Wanted(request).Select(item => item.Key.Value).Order().ToList();
        var ours = WorkoutKeys.Of(week, Sam).Values.Select(key => key.Value).Order().ToList();

        Assert.Equal(wanted, ours);
    }

    [Fact]
    public void ASecondSessionOfAKindIsTheNextOrdinal()
    {
        var week = Snapshot().Weeks[0];
        var keys = WorkoutKeys.Of(week, Sam);
        var weekIndex = PlacementKey.WeekIndexOf(Tue);
        var strengths = week.Plan.Plans[0].Sessions.Where(item => item.Session.Kind == Constitution.Domain.Workouts.SessionKind.Strength).ToList();

        Assert.Equal(PlacementKey.ForWorkout(Sam, Constitution.Domain.Workouts.SessionKind.Strength, weekIndex, 0), keys[strengths[0]]);
        Assert.Equal(PlacementKey.ForWorkout(Sam, Constitution.Domain.Workouts.SessionKind.Strength, weekIndex, 1), keys[strengths[1]]);
    }
}
