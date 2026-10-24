using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Tests.MealPlanning;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// C-6.4.1 · cooks, blocks and dinner time, with the real dinner planner's week: the cook is whoever is free (the planner's
/// choice, already in the week), the prep-and-cook block ends at dinner on the cook's primary calendar, and a slow-cooker meal
/// adds a morning "Start the slow cooker" event.
/// </summary>
public class CookAssignmentTests
{
    private static (MealWeekRequest Request, MealWeek Week) Planned(HouseholdCalendarSnapshot calendars, int seed = 3)
    {
        var request = MealData.Request(calendars: calendars, seed: seed);
        var week = MealPlanner.Plan(request);
        Assert.True(week.IsPlanned);
        return (request, week);
    }

    private static async Task<(Scenario Scenario, PlacementRun Run)> PlaceAsync(MealWeekRequest request, MealWeek week, MealPlacementOptions? options = null)
    {
        var scenario = new Scenario();
        var placement = MealCalendar.Request(week, request.Rules, request.Recipes, request.AdultTime, null, options);
        var run = await scenario.RunAsync(placement);
        Assert.Null(run.BlockedBecause);
        return (scenario, run);
    }

    [Fact]
    public async Task TheFreeAdultCooksAndHoldsTheBlock()
    {
        // Lee is at a recital over Tuesday's dinner, so whoever cooks on Tuesday is Sam.
        var calendars = Calendars(lee: [Entry("Recital", 1, (17, 0), (19, 0))]);
        var (request, week) = Planned(calendars);
        var tuesday = week.Dinners[1];
        Assert.Equal(DinnerKind.Cooked, tuesday.Kind);

        var (scenario, run) = await PlaceAsync(request, week);

        Assert.Equal(Sam, tuesday.Cook);
        var block = scenario.Calendar.By(MealCalendar.KeyOf(MealEventRole.Cook, Sam, Monday, tuesday.Day), Sam)!;
        Assert.Equal((tuesday.CookStart, tuesday.CookEnd), (block.Start, block.End));
        Assert.Equal(DinnerStart(1), block.End);
        Assert.DoesNotContain(scenario.Calendar.Of(Lee), item => EveningOf(item.Start) == 1);
        Assert.Empty(run.Plan!.Unplaced);
    }

    [Fact]
    public async Task EveryCookedEveningHasItsBlockOnTheCooksCalendar()
    {
        var (request, week) = Planned(Calendars());
        var cooked = week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked).ToList();
        Assert.True(cooked.Count >= 3, "The invented week should cook on several evenings.");

        var (scenario, run) = await PlaceAsync(request, week);

        Assert.Equal(cooked.Count, run.Created.Count);
        foreach (var dinner in cooked)
        {
            var block = scenario.Calendar.By(MealCalendar.KeyOf(MealEventRole.Cook, dinner.Cook!, Monday, dinner.Day), dinner.Cook!);
            Assert.NotNull(block);
            Assert.Equal((dinner.CookStart, dinner.CookEnd), (block.Start, block.End));
            Assert.StartsWith("Cook: ", block.Title, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ASlowCookerMealAddsAMorningStartOnTheCooksCalendar()
    {
        var (request, week) = Planned(Calendars());
        var options = new MealPlacementOptions { SlowCookerCards = request.Recipes.Select(card => card.Id).ToHashSet() };
        var cooked = week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked).ToList();

        var (scenario, run) = await PlaceAsync(request, week, options);

        Assert.Equal(cooked.Count * 2, run.Created.Count);
        foreach (var dinner in cooked)
        {
            var start = scenario.Calendar.By(MealCalendar.KeyOf(MealEventRole.StartSlowCooker, dinner.Cook!, Monday, dinner.Day), dinner.Cook!)!;
            Assert.Equal("Start the slow cooker", start.Title);
            Assert.True(start.End <= ZoneClock.At(dinner.Day, new TimeOnly(12, 0), request.Rules.Zone, ClockEdge.End), "The start is in the morning.");
            Assert.True(start.End <= dinner.CookStart);
        }
    }

    [Fact]
    public async Task TheSamePlannerWeekPlacedTwiceWritesOnce()
    {
        var (request, week) = Planned(Calendars());
        var (scenario, first) = await PlaceAsync(request, week);
        var snapshot = scenario.Calendar.Snapshot();

        var again = await scenario.RunAsync(MealCalendar.Request(week, request.Rules, request.Recipes, request.AdultTime));

        Assert.NotEmpty(first.Created);
        Assert.Empty(again.Created);
        Assert.Equal(snapshot, scenario.Calendar.Snapshot());
    }
}
