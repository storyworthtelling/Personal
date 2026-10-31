using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// B2: placement, the weekly run's index of events and the Workouts tab's names for them are three readers of one numbering. Once
/// numbers are issued they must read the same record, or a page, a swap and a skip each mean a different event.
/// </summary>
public class StableIndexTests
{
    private static PlannedSession Strength(int day) => Planned(day, (16, 0), (18, 0), Effort(40), Sam);

    private static KeyAssignments TheFirstWeek() =>
        SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty).Assignments;

    private static PlacementKey Key(int ordinal) => StrengthKey(Sam, ordinal);

    [Fact]
    public void TheIndexNamesADayByTheRecord()
    {
        var index = new PlacementIndex(PlanOf(Strength(2), Strength(4)), TheFirstWeek());

        Assert.Equal(Key(1), index.KeyFor(Sam, Day(2)));
        Assert.Equal(Key(2), index.KeyFor(Sam, Day(4)));
        Assert.Null(index.KeyFor(Sam, Day(0)));
    }

    [Fact]
    public void WithoutARecordTheIndexCountsAsItDid()
    {
        var index = new PlacementIndex(PlanOf(Strength(2), Strength(4)));

        Assert.Equal(Key(0), index.KeyFor(Sam, Day(2)));
        Assert.Equal(Key(1), index.KeyFor(Sam, Day(4)));
    }

    [Fact]
    public void TheIndexAndPlacementAgreeOnARecordAfterAReplan()
    {
        var record = TheFirstWeek();
        var plan = PlanOf(Strength(1), Strength(2), Strength(4));
        var calendars = Calendars();
        var request = new PlacementRequest(WeekStart, [BusyFor(Sam, calendars)], [], plan) { Assignments = record };

        var wanted = PlacementEngine.Wanted(request).Select(item => item.Key.Value).Order().ToList();
        var index = new PlacementIndex(plan, record);

        Assert.Equal(wanted, new[] { 1, 2, 4 }.Select(day => index.KeyFor(Sam, Day(day))!.Value.Value).Order().ToList());
        Assert.Equal(Key(3), index.KeyFor(Sam, Day(1)));
    }

    [Fact]
    public void TheTabsNamesAreTheRecordsToo()
    {
        var plan = PlanOf(Strength(2), Strength(4));
        var record = TheFirstWeek();
        var calendars = Calendars();
        var request = new PlacementRequest(WeekStart, [BusyFor(Sam, calendars)], [], plan) { Assignments = record };

        var wanted = PlacementEngine.Wanted(request).Select(item => item.Key.Value).Order().ToList();
        var ours = WorkoutKeys.Of(plan, Sam, record).Values.Select(key => key.Value).Order().ToList();

        Assert.Equal(wanted, ours);
        Assert.Equal([Key(1), Key(2)], WorkoutKeys.Of(plan, Sam, record).OrderBy(pair => pair.Key.Start).Select(pair => pair.Value));
    }

    [Fact]
    public void TheTabsNamesWithoutARecordAreTheOldCount()
    {
        var plan = PlanOf(Strength(2), Strength(4));

        Assert.Equal([Key(0), Key(1)], WorkoutKeys.Of(plan, Sam).OrderBy(pair => pair.Key.Start).Select(pair => pair.Value));
    }

    [Fact]
    public void ASessionIsNamedInWhicheverWeekHoldsItsDay()
    {
        var plan = PlanOf(Strength(2), Strength(4));
        var week = new WorkoutWeek(Request(Calendars(), null, now: At(0, 5)), plan);
        var record = TheFirstWeek();

        var key = WorkoutKeys.In([week], Sam, plan.Plans[0].Sessions[1], record);

        Assert.Equal(Key(2), key);
    }
}
