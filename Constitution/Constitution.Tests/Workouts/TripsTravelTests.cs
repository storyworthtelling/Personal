using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>A hotel-gym lookup the tests control, and a record of what it was asked.</summary>
internal sealed class FakeHotelLookup(Func<HotelStay, HotelGymResult> answer) : IHotelGymLookup
{
    public List<string> Asked { get; } = [];

    public Task<HotelGymResult> FindAsync(HotelStay hotel, CancellationToken cancellation)
    {
        Asked.Add(hotel.Name);
        return Task.FromResult(answer(hotel));
    }
}

/// <summary>What the whole pipeline hands back for a week: the plan the planner made, and what travel did to it.</summary>
internal sealed record TravelWeek(WorkoutPlanRequest Request, TravelPlan Travel, HouseholdWorkoutPlan Planned, TravelAdaptation Result)
{
    public WeeklyPlan SamPlan => Result.Plan.PlanFor(Sam)!;

    public WeeklyPlan LeePlan => Result.Plan.PlanFor(Lee)!;
}

internal static class TripsFixtures
{
    public static readonly EquipmentSet HotelKit = EquipmentSet.Of([EquipmentCatalogue.DumbbellsId, EquipmentCatalogue.BenchId, EquipmentCatalogue.TreadmillId]);

    /// <summary>An all-day entry from one day of the week to another, both inclusive (the calendar's exclusive end is worked out here).</summary>
    public static CalendarEntry AllDay(string title, int firstDay, int lastDay, Availability availability = Availability.Busy, IReadOnlyList<string>? guests = null) =>
        CalendarEntry.AllDay(title, Day(firstDay), Day(lastDay + 1), availability, guests);

    public static FakeHotelLookup NoGym() => new(hotel => new HotelGymResult.NotFound());

    public static FakeHotelLookup GymFound() => new(hotel => new HotelGymResult.Found(HotelKit));

    /// <summary>
    /// The pipeline a head runs: read the trips, resolve the hotels, show the planner calendars it can plan on, plan the
    /// week, then let travel adapt it.
    /// </summary>
    public static async Task<TravelWeek> RunAsync(HouseholdCalendarSnapshot calendars, IHotelGymLookup? lookup = null, EquipmentSet? equipment = null)
    {
        var rules = Rules();
        var read = TripDetector.Detect(rules, calendars, WeekStart, WeekStart.AddDays(6));
        var travel = await TravelPlan.ResolveAsync(read, lookup ?? NoGym(), CancellationToken.None);
        var aware = new TripAwareCalendars(calendars, rules);
        var request = new WorkoutPlanRequest(
            WeekStart,
            rules,
            [
                new PersonWeek(BusyTime.For(Sam, WeekStart, WeekStart.AddDays(6), rules, aware)),
                new PersonWeek(BusyTime.For(Lee, WeekStart, WeekStart.AddDays(6), rules, aware)),
            ],
            equipment ?? EquipmentSet.Everything(),
            Est(10, 30, 12));
        var planned = WorkoutPlanner.Plan(request);
        return new TravelWeek(request, travel, planned, TripAdapter.Apply(request, planned, travel));
    }

    /// <summary>Sam is free only on Wednesday to Friday (the other days are taken), so every session he has is on those days.</summary>
    public static IEnumerable<CalendarEntry> SamBusyExceptMidweek() => BlockDays(0, 1, 5);

    public static IReadOnlyList<PlannedSession> OnDays(WeeklyPlan plan, params int[] days) =>
        plan.Sessions.Where(session => days.Contains(DayIndex(session))).ToList();
}

/// <summary>
/// C-5.2.5 · Trips and holidays. A trip only the owner takes keeps the plan going, bodyweight in the room, outdoors, or at
/// the hotel gym if the calendar names a hotel and a lookup finds one; a family holiday pauses the plan and the Workouts
/// tab says so. Each test runs the whole pipeline a head would: detect, resolve, plan on calendars the planner can use,
/// adapt.
/// </summary>
public class TravelTests
{
    private static readonly string[] Midweek = ["Wednesday", "Thursday", "Friday"];

    private static HouseholdCalendarSnapshot SamAwayMidweek(params CalendarEntry[] extra) =>
        Calendars(sam: TripsFixtures.SamBusyExceptMidweek().Append(TripsFixtures.AllDay("Conference trip", 2, 4)).Concat(extra));

    // ---- WorkTripContinues ----

    [Fact]
    public async Task WorkTripContinues()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek());

        var onTheRoad = TripsFixtures.OnDays(week.SamPlan, 2, 3, 4);
        Assert.True(onTheRoad.Count >= 2, $"Sam's plan holds only {onTheRoad.Count} sessions on the trip days.");
        Assert.All(onTheRoad, session => Assert.Empty(session.Session.EquipmentNeeded));
        Assert.Empty(TripsFixtures.OnDays(week.SamPlan, 0, 1, 5));
        Assert.Contains(onTheRoad, session => session.Session.Kind == SessionKind.Strength);
        Assert.Empty(PlanRules.Violations(week.Request, week.Result.Plan));
    }

    [Fact]
    public async Task AWorkTripKeepsTheHomeEquipmentOutOfTheRoom()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek(), equipment: EquipmentSet.Everything());

        var strength = Assert.Single(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4), session => session.Session.Kind == SessionKind.Strength);
        Assert.DoesNotContain(strength.Session.Exercises, exercise => exercise.EquipmentId is not null);
        Assert.NotEmpty(strength.Session.Exercises);
    }

    [Fact]
    public async Task ARoadCardioDayIsOutdoorsNotOnATreadmill()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek());

        var cardio = Assert.Single(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4), session => SessionKinds.IsHard(session.Session.Kind) && session.Session.Kind != SessionKind.Strength);
        Assert.Equal(SessionKind.OutdoorCardio, cardio.Session.Kind);
    }

    [Fact]
    public async Task AnOpaqueTripBlocksTheWholeDayUnlessItIsShown()
    {
        var calendars = SamAwayMidweek();
        var raw = BusyFor(Sam, calendars);
        var aware = BusyTime.For(Sam, WeekStart, WeekStart.AddDays(6), Rules(), new TripAwareCalendars(calendars, Rules()));

        Assert.Empty(raw.FreeGaps(At(2, 6), At(2, 21), WorkoutPlanner.MinimumGap));
        Assert.NotEmpty(aware.FreeGaps(At(2, 6), At(2, 21), WorkoutPlanner.MinimumGap));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AMeetingOnTheTripStillBlocksItsHours()
    {
        var meeting = CalendarEntry.Timed("Client dinner", At(3, 16), At(3, 18));
        var week = await TripsFixtures.RunAsync(SamAwayMidweek(meeting));

        Assert.All(week.SamPlan.Sessions, session => Assert.False(session.Start < At(3, 18) && session.End > At(3, 16), "A session sits on top of the meeting."));
    }

    [Fact]
    public async Task TheOtherAdultIsNotOnTheRoad()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek());

        Assert.Same(week.Planned.PlanFor(Lee), week.Result.Plan.PlanFor(Lee));
        Assert.DoesNotContain(week.Result.Notes, note => note.Person == Lee);
    }

    [Fact]
    public async Task TheRoadIsNamedOnEachDay()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek());

        var notes = week.Result.Notes.Where(note => note.Person == Sam).ToList();
        Assert.NotEmpty(notes);
        Assert.All(notes, note => Assert.StartsWith("On the road:", note.Line));
        Assert.All(notes, note => Assert.Contains(note.Day, new[] { Day(2), Day(3), Day(4) }));
    }

    // ---- HotelGymOnlyWhenFound ----

    [Fact]
    public async Task HotelGymOnlyWhenFound()
    {
        var calendars = SamAwayMidweek(TripsFixtures.AllDay("Juniper Hotel", 2, 4));

        var found = await TripsFixtures.RunAsync(calendars, TripsFixtures.GymFound());
        var absent = await TripsFixtures.RunAsync(calendars, TripsFixtures.NoGym());
        var unknown = await TripsFixtures.RunAsync(calendars, new FakeHotelLookup(hotel => new HotelGymResult.Unavailable()));

        var strength = Assert.Single(TripsFixtures.OnDays(found.SamPlan, 2, 3, 4), session => session.Session.Kind == SessionKind.Strength);
        Assert.Contains(EquipmentCatalogue.DumbbellsId, strength.Session.EquipmentNeeded);
        foreach (var without in new[] { absent, unknown })
        {
            Assert.All(TripsFixtures.OnDays(without.SamPlan, 2, 3, 4), session => Assert.Empty(session.Session.EquipmentNeeded));
        }
    }

    [Fact]
    public async Task NoHotelInTheCalendarMeansNoLookup()
    {
        var lookup = TripsFixtures.GymFound();

        var week = await TripsFixtures.RunAsync(SamAwayMidweek(), lookup);

        Assert.Empty(lookup.Asked);
        Assert.All(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4), session => Assert.Empty(session.Session.EquipmentNeeded));
    }

    [Fact]
    public async Task AFoundGymIsNamedOnTheDay()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek(TripsFixtures.AllDay("Juniper Hotel", 2, 4)), TripsFixtures.GymFound());

        Assert.Contains(week.Result.Notes, note => note.Person == Sam && note.Line == "On the road: hotel gym");
    }

    [Fact]
    public async Task TheGymIsAskedAboutByTheNameTheCalendarGave()
    {
        var lookup = TripsFixtures.NoGym();

        await TripsFixtures.RunAsync(SamAwayMidweek(TripsFixtures.AllDay("Juniper Hotel", 2, 4)), lookup);

        Assert.Equal(["Juniper Hotel"], lookup.Asked);
    }

    [Fact]
    public async Task AHotelGymWithATreadmillTakesTheRoadCardio()
    {
        var week = await TripsFixtures.RunAsync(SamAwayMidweek(TripsFixtures.AllDay("Juniper Hotel", 2, 4)), TripsFixtures.GymFound());

        Assert.Contains(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4), session => session.Session.Kind == SessionKind.Treadmill);
    }

    // ---- FamilyHolidayPauses ----

    [Fact]
    public async Task FamilyHolidayPauses()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Family holiday", 2, 4)]);

        var week = await TripsFixtures.RunAsync(calendars);

        Assert.Empty(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4));
        Assert.Empty(TripsFixtures.OnDays(week.LeePlan, 2, 3, 4));
        Assert.NotEmpty(week.SamPlan.Sessions);
        Assert.NotEmpty(week.LeePlan.Sessions);
        Assert.Empty(week.Result.Plan.JointSessions.Where(session => DayIndex(session) is >= 2 and <= 4));
        Assert.All(week.Result.Notes, note => Assert.Equal("Paused: family holiday", note.Line));
        Assert.Equal(6, week.Result.Notes.Count);
    }

    [Fact]
    public async Task TheWorkoutsTabSaysSoOnEveryPausedDay()
    {
        var week = await TripsFixtures.RunAsync(Calendars(family: [TripsFixtures.AllDay("Family holiday", 2, 4)]));

        foreach (var person in new[] { Sam, Lee })
        {
            foreach (var day in new[] { 2, 3, 4 })
            {
                var note = Assert.Single(week.Result.Notes, item => item.Person == person && item.Day == Day(day));
                Assert.Equal("Paused: family holiday", note.Line);
            }
        }

        Assert.DoesNotContain(week.Result.Notes, note => note.Day == Day(0) || note.Day == Day(6));
    }

    [Fact]
    public async Task AFreeMarkedHolidayStillPauses()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Family holiday", 2, 4, Availability.Free)]);

        var week = await TripsFixtures.RunAsync(calendars);

        Assert.Empty(TripsFixtures.OnDays(week.SamPlan, 2, 3, 4));
        Assert.Empty(TripsFixtures.OnDays(week.LeePlan, 2, 3, 4));
        Assert.Equal(6, week.Result.Notes.Count);
    }

    [Fact]
    public async Task ADayOnlyOnOnePersonsCalendarPausesOnlyThem()
    {
        var calendars = Calendars(lee: [TripsFixtures.AllDay("Beach vacation", 2, 4)]);

        var week = await TripsFixtures.RunAsync(calendars);

        Assert.Empty(TripsFixtures.OnDays(week.LeePlan, 2, 3, 4));
        Assert.DoesNotContain(week.Result.Notes, note => note.Person == Sam);
        Assert.Equal(3, week.Result.Notes.Count(note => note.Person == Lee));
    }

    [Fact]
    public async Task AWholeWeekAwayExplainsTheEmptyPlan()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Family holiday", 0, 6)]);

        var week = await TripsFixtures.RunAsync(calendars);

        Assert.Empty(week.SamPlan.Sessions);
        Assert.Contains("holiday", week.SamPlan.NothingBecause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("holiday", week.LeePlan.NothingBecause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AHolidayAndATripOnTheSameDaysIsAHoliday()
    {
        var calendars = Calendars(
            sam: [TripsFixtures.AllDay("Conference trip", 2, 4)],
            family: [TripsFixtures.AllDay("Family holiday", 3, 3)]);

        var week = await TripsFixtures.RunAsync(calendars);

        Assert.Empty(TripsFixtures.OnDays(week.SamPlan, 3));
        Assert.Contains(week.Result.Notes, note => note.Person == Sam && note.Day == Day(3) && note.Line == "Paused: family holiday");
    }

    // ---- the pipeline over generated trips ----

    [Fact]
    public async Task TravelKeepsEveryPlannerRuleOverGeneratedWeeks()
    {
        var onTheRoad = 0;
        var paused = 0;
        // More weeks than it once took: only a clear work phrase on an own calendar leaves the day free to plan on the road
        // (review finding N1), so fewer of the generated weeks reach the adapter than when the family calendar could free a day too.
        for (var seed = 1; seed <= 400; seed++)
        {
            var random = new Random(seed);
            var first = random.Next(0, 6);
            var last = Math.Min(6, first + random.Next(0, 4));
            var kind = random.Next(3);
            var title = kind == 0 ? "Conference trip" : kind == 1 ? "Family holiday" : "Trip to the lake";
            var entry = TripsFixtures.AllDay(title, first, last);
            var calendars = random.Next(2) == 0
                ? Calendars(sam: [entry], family: random.Next(2) == 0 ? [TripsFixtures.AllDay("Juniper Hotel", first, last)] : [])
                : Calendars(family: [entry]);
            var equipment = EquipmentSet.Of(EquipmentCatalogue.All.Where(item => random.Next(2) == 0).Select(item => item.Id));

            var week = await TripsFixtures.RunAsync(calendars, random.Next(2) == 0 ? TripsFixtures.GymFound() : TripsFixtures.NoGym(), equipment);

            var where = $"seed {seed}";
            var violations = PlanRules.Violations(week.Request, week.Result.Plan)
                .Where(violation => violation.Rule != PlanRule.UntickedEquipment)
                .ToList();
            Assert.True(violations.Count == 0, $"{where}: {string.Join(", ", violations)}");
            foreach (var person in new[] { Sam, Lee })
            {
                foreach (var session in week.Result.Plan.PlanFor(person)!.Sessions)
                {
                    var trip = week.Travel.On(person, session.Day);
                    Assert.True(trip is null || trip.Kind == TripKind.Work, $"{where}: {person} has a session on a holiday day");
                    if (trip is not null)
                    {
                        onTheRoad++;
                        Assert.True(
                            session.Session.EquipmentNeeded.All(id => week.Travel.GymFor(trip)?.Has(id) == true),
                            $"{where}: a road session uses equipment that is not there");
                    }
                }
            }

            paused += week.Result.Notes.Count(note => note.Line == "Paused: family holiday");
        }

        Assert.True(onTheRoad > 10, $"Only {onTheRoad} sessions fell on the road; the test is not exercising the adapter.");
        Assert.True(paused > 10, $"Only {paused} days were paused.");
    }

    [Fact]
    public void NoTripNeedsAnythingFromTheNetwork()
    {
        var files = ConditionsAndTripsSources.Files();
        Assert.Contains(files, file => Path.GetFileName(file).StartsWith("Trips", StringComparison.Ordinal));
        Assert.DoesNotContain(files.Select(File.ReadAllText), text => text.Contains("HttpClient", StringComparison.Ordinal));
    }
}
