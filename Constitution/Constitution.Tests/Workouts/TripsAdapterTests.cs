using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// What travel does to a planned week, on weeks built by hand: a work trip rebuilds a session for the road, a holiday takes
/// it off, a joint session goes when anyone is away, and the Workouts tab gets a line for each day it matters.
/// </summary>
public class TripAdapterTests
{
    private const int Wednesday = 2;
    private const int Sunday = 6;

    private static readonly EquipmentSet MatAndTreadmill = EquipmentSet.Of([EquipmentCatalogue.MatId, EquipmentCatalogue.TreadmillId]);

    private static TravelPlan Away(TripKind kind, int first, int last, string person = Sam, string? hotel = null, HotelGymResult? gym = null)
    {
        var trip = new Trip(kind, Day(first), Day(last), hotel is null ? null : new HotelStay(hotel));
        var read = new TripRead(new Dictionary<string, IReadOnlyList<Trip>> { [person] = [trip] }, []);
        return hotel is null || gym is null
            ? TravelPlan.Of(read)
            : TravelPlan.Of(read, new Dictionary<string, HotelGymResult> { [hotel] = gym });
    }

    private static PlannedSession Strength(int day) =>
        ConditionsFixtures.Placed(
            new Session(
                SessionKind.Strength,
                TimeSpan.FromMinutes(40),
                false,
                [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Push-ups against a wall")]),
            day, (16, 30), (16, 0), (18, 0));

    private static PlannedSession Cardio(int day) => ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), day, (16, 30), (16, 0), (18, 0));

    private static PlannedSession Treadmill(int day) =>
        ConditionsFixtures.Placed(
            new Session(SessionKind.Treadmill, TimeSpan.FromMinutes(35), true, [new Exercise("Steady brisk walk on the treadmill", EquipmentCatalogue.TreadmillId)]),
            day, (16, 30), (16, 0), (18, 0));

    private static PlannedSession Mobility(int day) =>
        ConditionsFixtures.Placed(
            new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new Exercise("Cat and cow", EquipmentCatalogue.MatId)]),
            day, (16, 30), (16, 0), (18, 0));

    private static TravelAdaptation Adapt(TravelPlan travel, params PlannedSession[] sessions) =>
        TripAdapter.Apply(ConditionsFixtures.FreeWeek(MatAndTreadmill), ConditionsFixtures.Week(sessions), travel);

    // ---- a work trip: in the room ----

    [Fact]
    public void StrengthOnTheRoadNeedsNothingInTheRoom()
    {
        var original = Strength(Wednesday);

        var result = Adapt(Away(TripKind.Work, 2, 4), original);

        var change = Assert.Single(result.Changes);
        Assert.Equal(TravelAction.Rebuilt, change.Action);
        Assert.Equal("On the road: in the room", change.Line);
        var rebuilt = Assert.Single(SamOf(result.Plan).Sessions);
        Assert.Same(change.Replacement, rebuilt);
        Assert.Equal(SessionKind.Strength, rebuilt.Session.Kind);
        Assert.Empty(rebuilt.Session.EquipmentNeeded);
        Assert.Equal(original.Start, rebuilt.Start);
        Assert.Equal(original.Gap, rebuilt.Gap);
        Assert.Equal(original.Session.Duration, rebuilt.Session.Duration);
        Assert.Equal(original.Day, rebuilt.Day);
    }

    [Fact]
    public void MobilityLosesTheMatOnTheRoad()
    {
        var result = Adapt(Away(TripKind.Work, 2, 4), Mobility(Wednesday));

        var rebuilt = Assert.Single(SamOf(result.Plan).Sessions);
        Assert.Equal(SessionKind.Mobility, rebuilt.Session.Kind);
        Assert.Empty(rebuilt.Session.EquipmentNeeded);
    }

    [Fact]
    public void ATreadmillDayBecomesAnOutdoorOneInTheRoom()
    {
        var result = Adapt(Away(TripKind.Work, 2, 4), Treadmill(Wednesday));

        var rebuilt = Assert.Single(SamOf(result.Plan).Sessions);
        Assert.Equal(SessionKind.OutdoorCardio, rebuilt.Session.Kind);
        Assert.Empty(rebuilt.Session.EquipmentNeeded);
        Assert.Equal(TravelAction.Rebuilt, Assert.Single(result.Changes).Action);
        Assert.Equal(TimeSpan.FromMinutes(35), rebuilt.Session.Duration);
    }

    [Fact]
    public void OutdoorCardioOnTheRoadStaysAsItWas()
    {
        var original = Cardio(Wednesday);

        var result = Adapt(Away(TripKind.Work, 2, 4), original);

        Assert.Empty(result.Changes);
        Assert.Same(original, Assert.Single(SamOf(result.Plan).Sessions));
        var note = Assert.Single(result.Notes);
        Assert.Equal(new DayNote(Sam, Day(Wednesday), "On the road: outdoors"), note);
    }

    [Fact]
    public void ASundayWalkOnTheRoadIsLeftAlone()
    {
        var walk = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), Sunday, (10, 0), (9, 30), (12, 0));

        var result = Adapt(Away(TripKind.Work, 5, 6), walk);

        Assert.Empty(result.Changes);
        Assert.Same(walk, Assert.Single(SamOf(result.Plan).Sessions));
    }

    [Fact]
    public void ARoadSessionStaysInsideItsGap()
    {
        var result = Adapt(Away(TripKind.Work, 2, 4), Strength(Wednesday), Cardio(3), Mobility(4));

        Assert.All(SamOf(result.Plan).Sessions, session =>
        {
            Assert.True(session.Start >= session.Gap.Start);
            Assert.True(session.End <= session.Gap.End);
        });
    }

    [Fact]
    public void TheSameTripGivesTheSameRoadSession()
    {
        var first = Assert.Single(SamOf(Adapt(Away(TripKind.Work, 2, 4), Strength(Wednesday)).Plan).Sessions);
        var second = Assert.Single(SamOf(Adapt(Away(TripKind.Work, 2, 4), Strength(Wednesday)).Plan).Sessions);

        Assert.Equal(first.Session.Exercises, second.Session.Exercises);
    }

    // ---- a work trip: the hotel gym ----

    private static HotelGymResult.Found Gym(params string[] ids) => new(EquipmentSet.Of(ids));

    [Fact]
    public void AFoundGymLendsItsEquipmentToStrength()
    {
        var travel = Away(TripKind.Work, 2, 4, hotel: "Juniper Hotel", gym: Gym(EquipmentCatalogue.DumbbellsId, EquipmentCatalogue.BenchId));

        var result = Adapt(travel, Strength(Wednesday));

        var rebuilt = Assert.Single(SamOf(result.Plan).Sessions);
        Assert.Contains(EquipmentCatalogue.DumbbellsId, rebuilt.Session.EquipmentNeeded);
        Assert.All(rebuilt.Session.EquipmentNeeded, id => Assert.Contains(id, new[] { EquipmentCatalogue.DumbbellsId, EquipmentCatalogue.BenchId }));
        Assert.Equal("On the road: hotel gym", Assert.Single(result.Changes).Line);
    }

    [Fact]
    public void AGymWithATreadmillTakesTheCardioIndoors()
    {
        var travel = Away(TripKind.Work, 2, 4, hotel: "Juniper Hotel", gym: Gym(EquipmentCatalogue.TreadmillId));

        var result = Adapt(travel, Cardio(Wednesday));

        var rebuilt = Assert.Single(SamOf(result.Plan).Sessions);
        Assert.Equal(SessionKind.Treadmill, rebuilt.Session.Kind);
        Assert.Equal("On the road: hotel gym", Assert.Single(result.Changes).Line);
    }

    [Fact]
    public void AGymWithoutATreadmillLeavesTheCardioOutdoors()
    {
        var travel = Away(TripKind.Work, 2, 4, hotel: "Juniper Hotel", gym: Gym(EquipmentCatalogue.DumbbellsId));

        var result = Adapt(travel, Cardio(Wednesday));

        Assert.Equal(SessionKind.OutdoorCardio, Assert.Single(SamOf(result.Plan).Sessions).Session.Kind);
    }

    [Fact]
    public void AGymNotFoundIsTheRoom()
    {
        var travel = Away(TripKind.Work, 2, 4, hotel: "Juniper Hotel", gym: new HotelGymResult.NotFound());

        var result = Adapt(travel, Strength(Wednesday));

        Assert.Empty(Assert.Single(SamOf(result.Plan).Sessions).Session.EquipmentNeeded);
        Assert.Equal("On the road: in the room", Assert.Single(result.Changes).Line);
    }

    // ---- a holiday: paused ----

    [Fact]
    public void AHolidayTakesTheSessionsOff()
    {
        var result = Adapt(Away(TripKind.FamilyHoliday, 2, 4), Strength(1), Strength(Wednesday), Cardio(3), Mobility(5));

        Assert.Equal([Day(1), Day(5)], SamOf(result.Plan).Sessions.Select(session => session.Day).ToArray());
        Assert.Equal(2, result.Changes.Count);
        Assert.All(result.Changes, change =>
        {
            Assert.Equal(TravelAction.Paused, change.Action);
            Assert.Null(change.Replacement);
            Assert.Equal("Paused: family holiday", change.Line);
        });
    }

    [Fact]
    public void EveryHolidayDayInTheWeekIsNoted()
    {
        var result = Adapt(Away(TripKind.FamilyHoliday, 2, 4), Strength(1));

        Assert.Equal(
            [Day(2), Day(3), Day(4)],
            result.Notes.Select(note => note.Day).ToArray());
        Assert.All(result.Notes, note =>
        {
            Assert.Equal(Sam, note.Person);
            Assert.Equal("Paused: family holiday", note.Line);
        });
    }

    [Fact]
    public void OnlyTheDaysInTheWeekAreNoted()
    {
        var result = Adapt(Away(TripKind.FamilyHoliday, 5, 11), Strength(1));

        Assert.Equal([Day(5), Day(6)], result.Notes.Select(note => note.Day).ToArray());
    }

    [Fact]
    public void ATripOutsideTheWeekChangesNothing()
    {
        var plan = ConditionsFixtures.Week(Strength(1));

        var result = TripAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, Away(TripKind.Work, 8, 10));

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void NoTravelChangesNothing()
    {
        var plan = ConditionsFixtures.Week(Strength(1), Cardio(3));

        var result = TripAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, TravelPlan.None);

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void OnlyTheTravellersPlanChanges()
    {
        var lees = ConditionsFixtures.Placed(Strength(Wednesday).Session, Wednesday, (16, 30), (16, 0), (18, 0), Together.None, Lee);
        var plan = ConditionsFixtures.Week(Strength(Wednesday), lees);

        var result = TripAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, Away(TripKind.FamilyHoliday, 2, 4));

        Assert.Empty(SamOf(result.Plan).Sessions);
        Assert.Same(plan.PlanFor(Lee), result.Plan.PlanFor(Lee));
    }

    [Fact]
    public void AnEmptiedWeekSaysItIsPaused()
    {
        var result = Adapt(Away(TripKind.FamilyHoliday, 0, 6), Strength(1));

        var reason = SamOf(result.Plan).NothingBecause;
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("holiday", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWeekThatCouldNotBeReadKeepsItsOwnReason()
    {
        var unreadable = Request(CalendarsWith(sam: CalendarRead.Unreadable("Not shared yet.")));
        var plan = new HouseholdWorkoutPlan(
            WeekStart,
            [new WeeklyPlan(Sam, WeekStart, [], "Sam's calendars could not all be read, so no workouts were planned."), new WeeklyPlan(Lee, WeekStart, [], null)],
            []);

        var result = TripAdapter.Apply(unreadable, plan, Away(TripKind.FamilyHoliday, 0, 6));

        Assert.Equal("Sam's calendars could not all be read, so no workouts were planned.", SamOf(result.Plan).NothingBecause);
    }

    [Fact]
    public void AFullCalendarIsNotBlamedOnAOneDayHoliday()
    {
        const string NoRoom = "There was no free gap of 30 minutes or more in waking hours this week.";
        var plan = new HouseholdWorkoutPlan(
            WeekStart,
            [new WeeklyPlan(Sam, WeekStart, [], NoRoom), new WeeklyPlan(Lee, WeekStart, [], null)],
            []);

        var result = TripAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, Away(TripKind.FamilyHoliday, 2, 2));

        Assert.Equal(NoRoom, SamOf(result.Plan).NothingBecause);
    }

    [Fact]
    public void AWeekWithSessionsLeftKeepsNoReason()
    {
        var result = Adapt(Away(TripKind.FamilyHoliday, 2, 4), Strength(1), Strength(Wednesday));

        Assert.Null(SamOf(result.Plan).NothingBecause);
    }

    // ---- joint sessions ----

    private static PlannedSession FamilyWalk() =>
        ConditionsFixtures.Placed(ConditionsFixtures.Walk(TimeSpan.FromMinutes(40)), Sunday, (10, 0), (9, 30), (12, 0), Together.FamilyWalk, Sam, Lee);

    [Theory]
    [InlineData(TripKind.Work)]
    [InlineData(TripKind.FamilyHoliday)]
    public void AJointSessionGoesWhenAnyParticipantIsAway(TripKind kind)
    {
        var result = Adapt(Away(kind, 5, 6), FamilyWalk());

        var change = Assert.Single(result.Changes);
        Assert.Equal(TravelAction.DroppedJoint, change.Action);
        Assert.Equal("Dropped: someone is away", change.Line);
        Assert.Equal([Sam, Lee], change.People);
        Assert.Empty(result.Plan.JointSessions);
        Assert.Empty(SamOf(result.Plan).Sessions);
        Assert.Empty(LeeOf(result.Plan).Sessions);
    }

    [Fact]
    public void AJointSessionOnAnotherDayStays()
    {
        var walk = FamilyWalk();

        var result = Adapt(Away(TripKind.Work, 1, 2), walk);

        Assert.Empty(result.Changes);
        Assert.Same(walk, Assert.Single(result.Plan.JointSessions));
    }

    [Fact]
    public void AnEveningTogetherIsDroppedToo()
    {
        var evening = ConditionsFixtures.Placed(ConditionsFixtures.Lifting(), Wednesday, (20, 15), (20, 0), (21, 30), Together.AfterBedtime, Sam, Lee);

        var result = Adapt(Away(TripKind.Work, 2, 4), evening);

        Assert.Equal(TravelAction.DroppedJoint, Assert.Single(result.Changes).Action);
        Assert.Empty(result.Plan.JointSessions);
    }

    // ---- the notes ----

    [Fact]
    public void NotesComeInDayThenPersonOrder()
    {
        var read = new TripRead(
            new Dictionary<string, IReadOnlyList<Trip>>
            {
                [Lee] = [new Trip(TripKind.FamilyHoliday, Day(3), Day(3), null)],
                [Sam] = [new Trip(TripKind.FamilyHoliday, Day(2), Day(3), null)],
            },
            []);

        var result = TripAdapter.Apply(ConditionsFixtures.FreeWeek(), ConditionsFixtures.Week(), TravelPlan.Of(read));

        Assert.Equal(
            [(Day(2), Sam), (Day(3), Lee), (Day(3), Sam)],
            result.Notes.Select(note => (note.Day, note.Person)).ToArray());
    }

    [Fact]
    public void ALineNeverCarriesATitleOrADate()
    {
        var travel = Away(TripKind.Work, 2, 4, hotel: "Juniper Hotel", gym: Gym(EquipmentCatalogue.DumbbellsId));
        var result = Adapt(travel, Strength(Wednesday), Cardio(3));

        var lines = result.Changes.Select(change => change.Line).Concat(result.Notes.Select(note => note.Line)).ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain("Juniper", line);
            Assert.DoesNotContain(line, character => char.IsDigit(character));
        });
    }
}
