using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// C-6.4.1 through the real placement engine (C-5.3.2): the requests a week of dinners makes, placed on the invented household's
/// calendars. These are the dealbreaker's tests (R7): a meal is placed ahead of workouts, ends at dinner, and never lands on
/// busy or never-placed time, and a calendar nobody could look into is waited for rather than guessed.
/// </summary>
public class MealPlacementEngineTests
{
    // Sam stays at work until half past five, so a quarter past five is not his to give.
    private const string LateShift =
        "{\"Shifts\":[" +
        "{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"17:30\",\"Title\":\"Work\",\"Enabled\":true}," +
        "{\"Person\":\"Lee\",\"Start\":\"08:00\",\"End\":\"15:30\",\"Title\":\"Work\",\"Enabled\":true}]," +
        "\"OffKeywords\":[\"Off\",\"PTO\",\"Vacation\"]}";

    private static MealPlacementOptions Slow(params Recipe[] cards) =>
        new() { SlowCookerCards = cards.Select(card => card.Id).ToHashSet() };

    private static PlacementPlan Plan(PlacementRequest request) => PlacementEngine.Plan(request, PlacementFixture.Fresh);

    private static MealWeek FullWeek() => WeekOf(
        CookedOn(0, Chili, Sam, 45),
        CookedOn(1, Stew, Lee, 120),
        CookedOn(2, Pasta, Sam, 30),
        CookedOn(3, Chili, Lee, 45),
        CookedOn(4, Stew, Sam, 120),
        CookedOn(5, Pasta, Lee, 30),
        CookedOn(6, Chili, Sam, 45));

    // ---- meals first ----

    [Fact]
    public void MealsComeBeforeWorkouts()
    {
        // Lee's workout was proposed at twenty past five, on top of where the meal has to go.
        var session = Workout(1, (16, 0), (18, 0), (17, 20), 40, Lee);

        var plan = Plan(RequestFor(WeekOf(CookedOn(1, Chili, Lee, 45)), workouts: [session]));

        Assert.Equal("Cook: chili", plan.Outcomes[0].Title);
        var cook = PlacementFixture.Made(plan, "Cook: chili", Lee);
        Assert.Equal((Clock(1, 17, 15), Clock(1, 18)), (cook.Start, cook.End));
        var workout = Assert.Single(plan.ToCreate, item => item.Title.StartsWith("Workout", StringComparison.Ordinal));
        Assert.False(Overlap(workout.Start, workout.End, cook.Start, cook.End));
        Assert.Equal(Clock(1, 16), workout.Start);
    }

    [Fact]
    public void TheBlockLandsEndingAtDinner()
    {
        var plan = Plan(RequestFor(FullWeek()));

        Assert.Equal(7, plan.ToCreate.Count);
        Assert.Empty(plan.Unplaced);
        var chiliOnTheFirstEvening = Assert.Single(plan.ToCreate, item => item.Title == "Cook: chili" && item.Person == Sam && EveningOf(item.Start) == 0);
        Assert.Equal((Clock(0, 17, 15), Clock(0, 18)), (chiliOnTheFirstEvening.Start, chiliOnTheFirstEvening.End));
        Assert.All(plan.ToCreate, item => Assert.Equal(DinnerStart(EveningOf(item.Start)), item.End));
    }

    [Fact]
    public void EachBlockIsOnTheCooksCalendarAlone()
    {
        var plan = Plan(RequestFor(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Pasta, Lee, 30))));

        Assert.Equal(Sam, Assert.Single(plan.ToCreate, item => item.Title == "Cook: chili").Person);
        Assert.Equal(Lee, Assert.Single(plan.ToCreate, item => item.Title == "Cook: tomato pasta").Person);
        Assert.Equal(2, plan.Outcomes.Count);
    }

    // ---- never on blocked time ----

    [Fact]
    public void ABlockNeverOverlapsBusyTime()
    {
        // The planner found Sunday free; since then a call has been put over the block. Nothing is squeezed in.
        var calendars = Calendars(sam: [Entry("Call", 0, (17, 30), (17, 45))]);

        var plan = Plan(RequestFor(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Pasta, Lee, 30)), calendars));

        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal((Sam, UnplacedReason.NoRoom), (refused.Person, refused.Reason));
        Assert.Equal("Cook: chili", refused.Title);
        Assert.Equal("Cook: tomato pasta", Assert.Single(plan.ToCreate).Title);
    }

    [Fact]
    public void ABlockNeverOverlapsNeverPlacedTime()
    {
        // Work until half past five is never-placed time, not a calendar entry.
        var late = Rules(work: WorkSchedule.Read(LateShift));

        var plan = Plan(RequestFor(WeekOf(CookedOn(1, Chili, Sam, 45)), rules: late));

        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal(UnplacedReason.NoRoom, refused.Reason);
        Assert.Empty(plan.ToCreate);
    }

    [Fact]
    public void ABlockMayTouchWorkHoursAndDinner()
    {
        var late = Rules(work: WorkSchedule.Read(LateShift));

        var plan = Plan(RequestFor(WeekOf(CookedOn(1, Pasta, Sam, 30)), rules: late));

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal((Clock(1, 17, 30), Clock(1, 18)), (made.Start, made.End));
    }

    [Fact]
    public void TheFamilyDinnerStaysClear()
    {
        var plan = Plan(RequestFor(FullWeek(), options: Slow(Stew)));

        Assert.NotEmpty(plan.ToCreate);
        Assert.All(plan.ToCreate, item =>
        {
            var evening = EveningOf(item.Start);
            Assert.False(Overlap(item.Start, item.End, DinnerStart(evening), DinnerStart(evening) + House.Dinner.Length));
        });
    }

    // ---- the partner ----

    [Fact]
    public void ThePartnerIsNotDoubleBooked()
    {
        // A trip to the shop is asked for after the meals, over the same evening. Lee's hour is already spoken for.
        var shop = Errand("shop-a", "Groceries", 60, 1, (16, 30), (18, 0), Lee);

        var plan = Plan(RequestFor(WeekOf(CookedOn(1, Chili, Lee, 45), CookedOn(1 + 1, Pasta, Sam, 30)), after: shop));

        var cook = PlacementFixture.Made(plan, "Cook: chili", Lee);
        Assert.Equal((Clock(1, 17, 15), Clock(1, 18)), (cook.Start, cook.End));
        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal(("Groceries", UnplacedReason.NoRoom), (refused.Title, refused.Reason));
        Assert.Empty(plan.ToCreate.Where(item => item.Person == Sam && EveningOf(item.Start) == 1));
    }

    [Fact]
    public void NoTwoPlacedEventsOfOnePersonOverlap()
    {
        var shop = Errand("shop-a", "Groceries", 45, 3, (16, 0), (18, 0), Lee);

        var plan = Plan(RequestFor(FullWeek(), options: Slow(Stew), after: shop));

        var mine = plan.ToCreate.ToList();
        Assert.Contains(mine, item => item.Title == "Groceries");
        for (var first = 0; first < mine.Count; first++)
        {
            for (var second = first + 1; second < mine.Count; second++)
            {
                if (mine[first].Person == mine[second].Person)
                {
                    Assert.False(Overlap(mine[first].Start, mine[first].End, mine[second].Start, mine[second].End), $"{mine[first].Title} / {mine[second].Title}");
                }
            }
        }
    }

    [Fact]
    public async Task ACalendarNotSharedYetIsWaitedFor()
    {
        // R9: Lee has not shared her calendar. Nothing is written to it and nothing is concluded about it; Sam's meals go on.
        var scenario = new Scenario();
        scenario.Calendar.UnavailablePeople.Add(Lee);

        var run = await scenario.RunAsync(RequestFor(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Pasta, Lee, 30))));

        Assert.Equal("Cook: chili", Assert.Single(run.Created).Title);
        var waiting = Assert.Single(run.Plan!.Unplaced);
        Assert.Equal((Lee, UnplacedReason.CalendarUnavailable), (waiting.Person, waiting.Reason));
        Assert.Empty(scenario.Calendar.Of(Lee));
        Assert.Empty(run.Failed);
    }

    [Fact]
    public async Task AnUnreadableCalendarGetsNothingPlaced()
    {
        var scenario = new Scenario();
        var unreadable = CalendarsWith(lee: CalendarRead.Unreadable("Lee's calendar did not answer."));

        var run = await scenario.RunAsync(RequestFor(WeekOf(CookedOn(1, Pasta, Lee, 30)), unreadable));

        Assert.Empty(run.Created);
        Assert.Equal(UnplacedReason.CalendarUnreadable, Assert.Single(run.Plan!.Unplaced).Reason);
    }

    // ---- the slow cooker ----

    [Fact]
    public void ASlowCookerStartLandsBeforeWork()
    {
        var plan = Plan(RequestFor(WeekOf(CookedOn(2, Stew, Sam, 120)), options: Slow(Stew)));

        var start = PlacementFixture.Made(plan, "Start the slow cooker", Sam);
        Assert.Equal((Clock(2, 7), Clock(2, 7, 10)), (start.Start, start.End));
        var cook = PlacementFixture.Made(plan, "Cook: veggie stew", Sam);
        Assert.Equal((Clock(2, 16), Clock(2, 18)), (cook.Start, cook.End));
    }

    [Fact]
    public void ASlowCookerStartTriesTheNextMorningWindow()
    {
        // Seven to eight is taken and work starts at eight, so the first window has no room: the next one is tried.
        var calendars = Calendars(sam: [Entry("Dentist", 2, (7, 0), (8, 0))]);

        var plan = Plan(RequestFor(WeekOf(CookedOn(2, Stew, Sam, 120)), calendars, options: Slow(Stew)));

        var start = PlacementFixture.Made(plan, "Start the slow cooker", Sam);
        Assert.Equal((Clock(2, 6), Clock(2, 6, 10)), (start.Start, start.End));
    }

    [Fact]
    public void ASlowCookerStartWithNoMorningRoomIsNotSqueezed()
    {
        var calendars = Calendars(sam: [Entry("Away", 2, (5, 0), (12, 0))]);

        var plan = Plan(RequestFor(WeekOf(CookedOn(2, Stew, Sam, 120)), calendars, options: Slow(Stew)));

        Assert.Equal("Cook: veggie stew", Assert.Single(plan.ToCreate).Title);
        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal(("Start the slow cooker", UnplacedReason.NoRoom), (refused.Title, refused.Reason));
    }

    // ---- handing the week to the engine ----

    [Fact]
    public void TheWeekIsHandedOverMealsFirst()
    {
        var week = FullWeek();
        var people = PeopleFor(House, Calendars());
        var session = Workout(3, (16, 0), (18, 0), (16, 0), 30, Sam);

        var request = MealCalendar.Request(week, House, Cards, people, PlanOfSessions([session]), Slow(Stew));

        Assert.Equal(Sunday, request.WeekStart);
        Assert.Same(people, request.People);
        Assert.Equal(MealCalendar.Requests(week, House, Cards, Slow(Stew)).Select(item => item.Identity), request.HouseholdEvents.Select(item => item.Identity));
        Assert.NotNull(request.Workouts);
        Assert.Equal(9, request.HouseholdEvents.Count);
    }
}
