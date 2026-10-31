using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The pipeline needs to know which calendar event belongs to which person's day, and placement does not say. The index
/// works the keys out the way placement does (the person's sessions of a kind, in time order), so these tests hold it
/// to placement itself: the keys placement asks for and the keys the index names must be the same set.
/// </summary>
public class PlacementIndexTests
{
    [Fact]
    public void OrdinalsStartAtZeroPerKind()
    {
        var plan = PlanOf(
            Planned(0, (16, 0), (18, 0), Effort(40), Sam),
            Planned(2, (16, 0), (18, 0), Effort(40), Sam));

        var index = new PlacementIndex(plan);

        Assert.Equal(StrengthKey(Sam, 0), index.KeyFor(Sam, Day(0)));
        Assert.Equal(StrengthKey(Sam, 1), index.KeyFor(Sam, Day(2)));
    }

    [Fact]
    public void OrdinalsCountEachKindOnItsOwn()
    {
        var plan = PlanOf(
            Planned(0, (16, 0), (18, 0), Effort(40), Sam),
            Planned(1, (16, 0), (18, 0), Effort(20, sweaty: false, kind: SessionKind.Mobility), Sam),
            Planned(2, (16, 0), (18, 0), Effort(40), Sam));

        var index = new PlacementIndex(plan);

        Assert.Equal(MobilityKey(Sam, 0), index.KeyFor(Sam, Day(1)));
        Assert.Equal(StrengthKey(Sam, 1), index.KeyFor(Sam, Day(2)));
    }

    [Fact]
    public void ADayWithNoSessionHasNoKey()
    {
        var plan = PlanOf(Planned(0, (16, 0), (18, 0), Effort(40), Sam));

        Assert.Null(new PlacementIndex(plan).KeyFor(Sam, Day(3)));
        Assert.Null(new PlacementIndex(plan).KeyFor(Lee, Day(0)));
    }

    [Fact]
    public void ANameIsMatchedWithoutCase()
    {
        var plan = PlanOf(Planned(0, (16, 0), (18, 0), Effort(40), Sam));

        Assert.Equal(StrengthKey(Sam, 0), new PlacementIndex(plan).KeyFor("SAM", Day(0)));
    }

    [Fact]
    public void AJointSessionKeysForEachPerson()
    {
        var walk = Effort(30, sweaty: false, kind: SessionKind.GentleWalk);
        var plan = PlanOf(
            Planned(2, (16, 0), (18, 0), Effort(20, sweaty: false, kind: SessionKind.GentleWalk), Sam),
            PlannedAt(6, (14, 0), (16, 0), (14, 0), walk, Together.FamilyWalk, Sam, Lee));

        var index = new PlacementIndex(plan);

        var sam = index.KeyFor(Sam, Day(6));
        var lee = index.KeyFor(Lee, Day(6));
        Assert.NotNull(sam);
        Assert.NotNull(lee);
        Assert.Equal(PlacementKey.ForWorkout(Sam, SessionKind.GentleWalk, Week, 1), sam);
        Assert.Equal(PlacementKey.ForWorkout(Lee, SessionKind.GentleWalk, Week, 0), lee);
    }

    [Fact]
    public void AJointSessionHeldOnlyInTheJointListStillGetsKeys()
    {
        var walk = Effort(30, sweaty: false, kind: SessionKind.GentleWalk);
        var joint = PlannedAt(6, (14, 0), (16, 0), (14, 0), walk, Together.FamilyWalk, Sam, Lee);
        var plan = new HouseholdWorkoutPlan(
            WeekStart,
            [new WeeklyPlan(Sam, WeekStart, [], null), new WeeklyPlan(Lee, WeekStart, [], null)],
            [joint]);
        var calendars = Calendars();
        var request = new PlacementRequest(WeekStart, [BusyFor(Sam, calendars), BusyFor(Lee, calendars)], [], plan);

        var wanted = PlacementEngine.Wanted(request).Select(item => item.Key).Order(KeyOrder).ToList();
        var index = new PlacementIndex(plan);

        Assert.Equal(2, wanted.Count);
        Assert.Equal(wanted, new[] { index.KeyFor(Sam, Day(6)), index.KeyFor(Lee, Day(6)) }.OfType<PlacementKey>().Order(KeyOrder).ToList());
    }

    [Fact]
    public void TheKeysAreExactlyTheOnesPlacementAsksFor()
    {
        foreach (var calendars in InventedWeeks())
        {
            var plan = Plan(calendars);
            Assert.NotEmpty(plan.Plans.SelectMany(week => week.Sessions));
            var request = new PlacementRequest(WeekStart, [BusyFor(Sam, calendars), BusyFor(Lee, calendars)], [], plan);

            var wanted = PlacementEngine.Wanted(request).Select(item => item.Key).Order(KeyOrder).ToList();
            var index = new PlacementIndex(plan);
            var named = plan.Plans
                .SelectMany(week => week.Sessions.Select(session => index.KeyFor(week.Person, session.Day)))
                .Select(key => key ?? throw new Xunit.Sdk.XunitException("A planned session had no key."))
                .Distinct()
                .Order(KeyOrder)
                .ToList();

            Assert.Equal(wanted, named);
        }
    }

    [Fact]
    public void EverySessionOfAGeneratedWeekGetsItsOwnKey()
    {
        var random = new Random(20261102);
        for (var round = 0; round < 40; round++)
        {
            var samBlocked = Enumerable.Range(0, 7).Where(_ => random.Next(3) == 0).ToArray();
            var leeBlocked = Enumerable.Range(0, 7).Where(_ => random.Next(3) == 0).ToArray();
            var calendars = Calendars(sam: BlockDays(samBlocked), lee: BlockDays(leeBlocked));
            var plan = Plan(calendars);
            var request = new PlacementRequest(WeekStart, [BusyFor(Sam, calendars), BusyFor(Lee, calendars)], [], plan);

            var wanted = PlacementEngine.Wanted(request).Select(item => item.Key).Order(KeyOrder).ToList();
            var index = new PlacementIndex(plan);
            var named = plan.Plans
                .SelectMany(week => week.Sessions.Select(session => index.KeyFor(week.Person, session.Day)))
                .OfType<PlacementKey>()
                .Distinct()
                .Order(KeyOrder)
                .ToList();

            Assert.True(wanted.SequenceEqual(named), $"Round {round}: placement wants {wanted.Count} keys and the index named {named.Count}.");
        }
    }

    private static IEnumerable<HouseholdCalendarSnapshot> InventedWeeks()
    {
        yield return Calendars();
        yield return Calendars(sam: BlockDays(0, 1), lee: BlockDays(3));
        yield return Calendars(family: [CalendarEntry.Timed("Visit", At(5, 9), At(5, 12))]);
    }

    private static readonly IComparer<PlacementKey> KeyOrder = Comparer<PlacementKey>.Create((left, right) => string.CompareOrdinal(left.Value, right.Value));
}
