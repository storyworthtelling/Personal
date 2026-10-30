using System.Globalization;
using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Calendar;
using Constitution.Tests.Calendar.Google;
using Constitution.Tests.MealPlanning;
using Constitution.Tests.Placement;
using Constitution.Tests.Setups;
using Google.Apis.Calendar.v3;
using static Constitution.Tests.Calendar.InventedFamily;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.CrossRun;

/// <summary>What the two runs are asked to place in one invented fortnight: the meal week, the workouts of its own week, and the workouts of the week before it (the Groceries trip reaches back into that one).</summary>
internal sealed record CrossRunScenario(
    int Seed,
    PlanningRules Rules,
    MealWeek Meals,
    IReadOnlyList<Recipe> Cards,
    MealPlacementOptions Options,
    HouseholdWorkoutPlan WorkoutsOfTheWeek,
    HouseholdWorkoutPlan WorkoutsOfTheWeekBefore,
    IReadOnlyDictionary<string, List<CalendarEntry>> Life)
{
    public static readonly DateOnly WeekBefore = Sunday.AddDays(-7);

    public static readonly DateTimeOffset ApprovedAt = Edt(10, 22, 8);

    private static readonly string[] Titles = ["Dentist", "Call", "Plumber", "Recital", "Haircut", "Class", "Robin soccer, Lee drives", "Juniper swimming"];

    private static readonly SessionKind[] Kinds = [SessionKind.Strength, SessionKind.Mobility, SessionKind.GentleWalk, SessionKind.Treadmill, SessionKind.OutdoorCardio];

    public static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.Year, day.Month, day.Day, hour, minute, 0, TimeSpan.FromHours(-4));

    /// <summary>One invented fortnight, or null when the planner could not plan the meal week (nothing to place).</summary>
    public static CrossRunScenario? Generate(int seed)
    {
        var random = new Random(seed);
        var cards = Enumerable.Range(0, random.Next(10, 19))
            .Select(index => MealData.Dish("dish-" + index, Cuisines.All[random.Next(Cuisines.All.Count)], forKids: random.Next(3) == 0, minutes: random.Next(5, 200)))
            .ToList();
        var sam = LifeEntries(random, 9);
        var lee = LifeEntries(random, 9);
        var family = LifeEntries(random, 4);
        var week = MealPlanner.Plan(MealData.Request(Within(sam), Within(lee), Within(family), cards, seed: seed));
        if (!week.IsPlanned)
        {
            return null;
        }

        var options = new MealPlacementOptions { SlowCookerCards = cards.Where(_ => random.Next(3) == 0).Select(card => card.Id).ToHashSet() };
        var life = new Dictionary<string, List<CalendarEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sam@example.com"] = sam,
            ["lee@example.com"] = lee,
            ["family@example.com"] = family,
        };
        return new CrossRunScenario(
            seed,
            Calendar.InventedFamily.Rules(),
            week,
            cards,
            options,
            Sessions(random, Sunday, 0, 6, 6, 21, random.Next(4, 11)),
            Sessions(random, WeekBefore, 4, 6, 8, 18, random.Next(2, 6)),
            life);
    }

    // The meal planner is shown the calendars of its own week; the fortnight's earlier days are for the trip and the workouts.
    private static List<CalendarEntry> Within(List<CalendarEntry> entries) =>
        entries.Where(entry => entry.FirstDay >= Sunday).ToList();

    private static List<CalendarEntry> LifeEntries(Random random, int most)
    {
        var entries = new List<CalendarEntry>();
        var count = random.Next(0, most);
        for (var index = 0; index < count; index++)
        {
            var day = WeekBefore.AddDays(random.Next(0, 14));
            var from = random.Next(5, 21);
            var to = Math.Min(23, from + random.Next(1, 4));
            var minute = random.Next(0, 2) * 30;
            var start = At(day, from, minute);
            entries.Add(CalendarEntry.Timed(Titles[random.Next(Titles.Length)], start, start.AddHours(to - from)));
        }

        return entries;
    }

    private static HouseholdWorkoutPlan Sessions(Random random, DateOnly weekStart, int firstDay, int lastDay, int fromHour, int toHour, int count)
    {
        var sessions = new List<PlannedSession>();
        for (var index = 0; index < count; index++)
        {
            var day = weekStart.AddDays(random.Next(firstDay, lastDay + 1));
            var minutes = random.Next(20, 61);
            var open = At(day, random.Next(fromHour, toHour - 3), random.Next(0, 2) * 30);
            var gap = new FreeGap(open, open.AddMinutes(random.Next(minutes, 241)));
            var person = random.Next(2) == 0 ? Sam : Lee;
            var effort = PlacementFixture.Effort(minutes, random.Next(2) == 0, Kinds[random.Next(Kinds.Length)]);
            sessions.Add(new PlannedSession(day, open, gap, effort, [person], Together.None));
        }

        var people = sessions.SelectMany(session => session.Participants).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var plans = people
            .Select(person => new WeeklyPlan(person, weekStart, sessions.Where(session => session.Participants.Contains(person)).OrderBy(session => session.Start).ToList(), null))
            .ToList();
        return new HouseholdWorkoutPlan(weekStart, plans, []);
    }
}

/// <summary>
/// One shared calendar the two runs write to and read back through the real Google source: whatever the fake calendar holds is served as
/// Google events (the app's own carrying the private key), beside the invented life that was there already. The record of written keys and of
/// issued numbers are the two runs' shared ones. <c>blind</c> reads as the code did before the families: each run dropped every event
/// that carried a key, so it could not see the other's.
/// </summary>
internal sealed class CrossRunWorld
{
    private readonly FakeCalendarHttp http = new();
    private readonly CrossRunScenario scenario;
    private readonly bool blind;

    public CrossRunWorld(CrossRunScenario scenario, bool blind)
    {
        this.scenario = scenario;
        this.blind = blind;
        http.Reply = request =>
        {
            var id = Uri.UnescapeDataString(request.Path).Split("/calendars/")[1].Split("/events")[0];
            var items = new List<string>();
            if (scenario.Life.TryGetValue(id, out var life))
            {
                items.AddRange(life.Select(entry => EventJson("life", entry.Title, entry.Start, entry.End, null)));
            }

            if (PersonOf(id) is { } person)
            {
                items.AddRange(Calendar.Of(person).Select(stored => EventJson(stored.Id, stored.Title, stored.Start, stored.End, stored.Key)));
            }

            return FakeCalendarHttp.Json("{\"kind\":\"calendar#events\",\"items\":[" + string.Join(",", items) + "]}");
        };
    }

    public FakeCalendar Calendar { get; } = new();

    public MemoryKeyStore Keys { get; } = new();

    public MemoryKeyAssignments Issued { get; } = new();

    private static string? PersonOf(string calendarId) => calendarId switch
    {
        "sam@example.com" => Sam,
        "lee@example.com" => Lee,
        _ => null,
    };

    private static string EventJson(string id, string title, DateTimeOffset start, DateTimeOffset end, PlacementKey? key)
    {
        string Instant(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        return "{\"id\":" + JsonSerializer.Serialize(id) + ",\"status\":\"confirmed\",\"summary\":" + JsonSerializer.Serialize(title)
            + ",\"start\":{\"dateTime\":\"" + Instant(start) + "\"},\"end\":{\"dateTime\":\"" + Instant(end) + "\"}"
            + (key is { } held ? ",\"extendedProperties\":{\"private\":{\"constitutionKey\":" + JsonSerializer.Serialize(held.Value) + "}}" : string.Empty) + "}";
    }

    private Task<HouseholdCalendarSnapshot> ReadAsync(KeyFamily ownFamily, DateTimeOffset from, DateTimeOffset to)
    {
        var service = http.Service();
        var source = new GoogleHouseholdCalendars(
            _ => Task.FromResult<CalendarService>(service),
            () => new HouseholdSetupRead.Configured(HouseholdData.Setup()),
            blind ? null : ownFamily);
        return source.ReadAsync(from, to, CancellationToken.None);
    }

    /// <summary>The meal run's placing half, as <c>MealWeekRun</c> does it: the calendars from three days before the Sunday, plain (not trip-aware), cook blocks, morning starts and the trip through one runner.</summary>
    public async Task<PlacementRun> MealRunAsync()
    {
        var rules = scenario.Rules;
        var first = Sunday;
        var reach = first.AddDays(-GroceryTripOptions.DefaultDaysBefore);
        var calendars = await ReadAsync(KeyFamily.HouseholdMeal, ZoneClock.StartOfDay(reach, rules.Zone), ZoneClock.StartOfDay(first.AddDays(MealWeek.Evenings), rules.Zone));
        var busy = rules.Adults.Select(adult => BusyTime.For(adult.Name, reach, first.AddDays(MealWeek.Evenings - 1), rules, calendars)).ToList();
        var events = MealCalendar.Requests(scenario.Meals, rules, scenario.Cards, scenario.Options).ToList();
        if (GroceryPlacement.Request(first, rules, busy, CrossRunScenario.ApprovedAt, new GroceryTripOptions()) is { } trip)
        {
            events.Add(trip);
        }

        return await new PlacementRunner(Calendar, Keys, Issued).RunAsync(new PlacementRequest(first, busy, events, null), CancellationToken.None);
    }

    /// <summary>The workout run's placing half, as <c>WorkoutWeekRun</c> does it: the week's calendars read trip-aware, then the plan through one runner.</summary>
    public async Task<PlacementRun> WorkoutRunAsync(DateOnly weekStart, HouseholdWorkoutPlan plan)
    {
        var rules = scenario.Rules;
        var calendars = await ReadAsync(KeyFamily.Workout, ZoneClock.StartOfDay(weekStart, rules.Zone), ZoneClock.StartOfDay(weekStart.AddDays(7), rules.Zone));
        var aware = new TripAwareCalendars(calendars, rules);
        var busy = rules.Adults.Select(adult => BusyTime.For(adult.Name, weekStart, weekStart.AddDays(6), rules, aware)).ToList();
        var request = new PlacementRequest(weekStart, busy, [], plan) { Options = new PlacementOptions { ShowerBuffer = PlacementFixture.ShowerBuffer } };
        return await new PlacementRunner(Calendar, Keys, Issued).RunAsync(request, CancellationToken.None);
    }

    /// <summary>Both runs' three placements, in the order given: meals first (the order the weeks are due in), or the workouts first.</summary>
    public async Task<IReadOnlyList<PlacementRun>> RunAllAsync(bool mealsFirst)
    {
        var runs = new List<PlacementRun>();
        if (mealsFirst)
        {
            runs.Add(await MealRunAsync());
        }

        runs.Add(await WorkoutRunAsync(CrossRunScenario.WeekBefore, scenario.WorkoutsOfTheWeekBefore));
        runs.Add(await WorkoutRunAsync(Sunday, scenario.WorkoutsOfTheWeek));
        if (!mealsFirst)
        {
            runs.Add(await MealRunAsync());
        }

        return runs;
    }

    /// <summary>What the calendar holds now, as the numbers the properties are about.</summary>
    public CrossRunReading Read()
    {
        var meals = 0;
        var workouts = 0;
        var cross = 0;
        var onLife = 0;
        foreach (var person in new[] { Sam, Lee })
        {
            var mine = Calendar.Of(person);
            var cooking = mine.Where(item => KeyFamilies.Of(item.Key) == KeyFamily.HouseholdMeal).ToList();
            var training = mine.Where(item => KeyFamilies.Of(item.Key) == KeyFamily.Workout).ToList();
            meals += cooking.Count;
            workouts += training.Count;
            cross += training.Count(work => cooking.Any(meal => work.Start < meal.End && meal.Start < work.End));
            var life = scenario.Life[person.Equals(Sam, StringComparison.Ordinal) ? "sam@example.com" : "lee@example.com"];
            onLife += mine.Count(item => life.Any(entry => item.Start < entry.End && entry.Start < item.End));
        }

        return new CrossRunReading(meals, workouts, cross, onLife);
    }

    /// <summary>The calendar and both records as comparable text, to prove a second pass changed nothing.</summary>
    public string Fingerprint() => string.Join("\n--\n", Calendar.CreateCalls, Calendar.Snapshot(), Keys.Json, Issued.Json);
}

/// <summary>Workout events that overlap a meal event on the same calendar, and events of either run that sit on the owner's own life.</summary>
internal sealed record CrossRunReading(int Meals, int Workouts, int WorkoutsOnMeals, int OnTheOwnersLife);
