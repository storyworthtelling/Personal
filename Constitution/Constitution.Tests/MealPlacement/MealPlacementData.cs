using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// The invented week the meal placement tests stand on: the Meals tests' Sunday 25 October 2026 to Saturday 31 October, all on
/// daylight time (UTC-4; the clocks go back the Sunday after), with Sam and Lee, dinner from 6 to 7 pm, Sam at work from 8 to 4 and Lee from 8 to
/// 3:30 on weekdays. Weeks are written by hand rather than asked of the planner, so each test controls exactly the evening it is
/// about; the cook assignment tests and the property test do run the real planner's weeks through. Every expected instant is
/// written out on the clock.
/// </summary>
internal static class MealPlacementData
{
    public static readonly Recipe Chili = Card("chili", "chili", 45);

    public static readonly Recipe Pasta = Card("tomato-pasta", "tomato pasta", 30);

    public static readonly Recipe Stew = Card("veggie-stew", "veggie stew", 120);

    public static readonly IReadOnlyList<Recipe> Cards = [Chili, Pasta, Stew];

    public static readonly int Week = PlacementKey.WeekIndexOf(Sunday);

    public static PlanningRules House => Rules();

    public static Recipe Card(string id, string title, int minutes) => new(
        new RecipeId(id),
        title,
        Cuisine.Other,
        [new Ingredient("1", "cup", "water")],
        [new RecipeStep("Cook it.", minutes, true)],
        4,
        null,
        false);

    /// <summary>The wall-clock time on the evening <paramref name="dayOffset"/> days after the week's Sunday (0 is Sunday, 6 is Saturday).</summary>
    public static DateTimeOffset Clock(int dayOffset, int hour, int minute = 0) => Edt(10, 25 + dayOffset, hour, minute);

    public static DateTimeOffset DinnerStart(int dayOffset) => Clock(dayOffset, 18);

    /// <summary>A cooked dinner whose block ends exactly at the family dinner, the way the planner writes it.</summary>
    public static DinnerChoice CookedOn(int dayOffset, Recipe card, string cook, int minutes) =>
        DinnerChoice.Cooked(
            Sunday.AddDays(dayOffset),
            card.Id,
            cook,
            DinnerStart(dayOffset) - TimeSpan.FromMinutes(minutes),
            DinnerStart(dayOffset),
            false,
            false,
            "From the box.");

    /// <summary>A planned week from the evenings given; every other evening is a plain nothing.</summary>
    public static MealWeek WeekOf(params DinnerChoice[] placed) =>
        MealWeek.Planned(
            Sunday,
            Enumerable.Range(0, MealWeek.Evenings).Select(offset =>
                placed.FirstOrDefault(dinner => dinner.Day == Sunday.AddDays(offset))
                ?? DinnerChoice.NothingPlanned(Sunday.AddDays(offset), "Nothing is planned.")));

    public static List<BusyTime> PeopleFor(PlanningRules rules, HouseholdCalendarSnapshot calendars) =>
        rules.Adults.Select(adult => BusyTime.For(adult.Name, Sunday, Sunday.AddDays(6), rules, calendars)).ToList();

    /// <summary>The week handed to the engine, meals first, with whatever else the test wants requested after them.</summary>
    public static PlacementRequest RequestFor(
        MealWeek week,
        HouseholdCalendarSnapshot? calendars = null,
        PlanningRules? rules = null,
        MealPlacementOptions? options = null,
        IReadOnlyList<PlannedSession>? workouts = null,
        params HouseholdEventRequest[] after)
    {
        var house = rules ?? House;
        var people = PeopleFor(house, calendars ?? Calendars());
        var meals = MealCalendar.Requests(week, house, Cards, options);
        var plan = workouts is null ? null : PlanOfSessions(workouts);
        return new PlacementRequest(week.WeekStart, people, [.. meals, .. after], plan);
    }

    public static HouseholdWorkoutPlan PlanOfSessions(IReadOnlyList<PlannedSession> sessions)
    {
        var names = sessions.SelectMany(session => session.Participants).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var plans = names
            .Select(name => new WeeklyPlan(name, Sunday, sessions.Where(session => session.Participants.Contains(name)).ToList(), null))
            .ToList();
        return new HouseholdWorkoutPlan(Sunday, plans, []);
    }

    /// <summary>A workout proposed at <paramref name="proposed"/> inside the gap, a non-sweaty strength session of the given length.</summary>
    public static PlannedSession Workout(int dayOffset, (int Hour, int Minute) gapFrom, (int Hour, int Minute) gapTo, (int Hour, int Minute) proposed, int minutes, string person) =>
        new(
            Sunday.AddDays(dayOffset),
            Clock(dayOffset, proposed.Hour, proposed.Minute),
            new FreeGap(Clock(dayOffset, gapFrom.Hour, gapFrom.Minute), Clock(dayOffset, gapTo.Hour, gapTo.Minute)),
            PlacementFixture.Effort(minutes, sweaty: false),
            [person],
            Together.None);

    public static HouseholdEventRequest Errand(string identity, string title, int minutes, int dayOffset, (int Hour, int Minute) from, (int Hour, int Minute) to, params string[] people) =>
        new(
            identity,
            title,
            TimeSpan.FromMinutes(minutes),
            people,
            [new PlacementWindow(Clock(dayOffset, from.Hour, from.Minute), Clock(dayOffset, to.Hour, to.Minute))]);

    public static CalendarEntry Entry(string title, int dayOffset, (int Hour, int Minute) from, (int Hour, int Minute) to) =>
        CalendarEntry.Timed(title, Clock(dayOffset, from.Hour, from.Minute), Clock(dayOffset, to.Hour, to.Minute));

    /// <summary>The local evening (0 to 6) an instant falls on.</summary>
    public static int EveningOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.DateTime).DayNumber - Sunday.DayNumber;

    /// <summary>True when the two half-open stretches share any time; touching is not overlapping.</summary>
    public static bool Overlap(DateTimeOffset firstStart, DateTimeOffset firstEnd, DateTimeOffset secondStart, DateTimeOffset secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    /// <summary>The same runner the head will use, against the fake calendar and a record held as stored text.</summary>
    public sealed class Scenario
    {
        public FakeCalendar Calendar { get; } = new();

        public MemoryKeyStore Store { get; } = new();

        public Task<PlacementRun> RunAsync(PlacementRequest request) =>
            new PlacementRunner(Calendar, Store).RunAsync(request, CancellationToken.None);
    }
}
