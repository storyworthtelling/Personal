using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// C-5.3.3 for meals: the owner's edits win, silently. These run the real runner against the fake calendar, which can also play the
/// owner, who moves and deletes events in the Google Calendar app. The week: Sam's chili on Sunday, Lee's slow-cooker stew on
/// Tuesday (a cook block and a morning start) and Sam's pasta on Wednesday.
/// </summary>
public class MealOwnerEditTests
{
    private static MealPlacementOptions SlowStew => new() { SlowCookerCards = new HashSet<RecipeId> { Stew.Id } };

    private static MealPlanWeek TheWeek => new(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Stew, Lee, 120), CookedOn(2, Pasta, Sam, 30)));

    private static PlacementKey CookKey(string person, int evening) => MealCalendar.KeyOf(MealEventRole.Cook, person, Sunday, Sunday.AddDays(evening));

    private static PlacementKey StartKey(string person, int evening) => MealCalendar.KeyOf(MealEventRole.StartSlowCooker, person, Sunday, Sunday.AddDays(evening));

    private static async Task<Scenario> FirstRunAsync()
    {
        var scenario = new Scenario();
        var run = await scenario.RunAsync(TheWeek.Request());
        Assert.Equal(4, run.Created.Count);
        return scenario;
    }

    [Fact]
    public async Task TheFirstRunWritesEveryMeal()
    {
        var scenario = await FirstRunAsync();

        Assert.Equal(4, scenario.Calendar.All.Count);
        Assert.Equal(["Cook: chili", "Cook: tomato pasta"], scenario.Calendar.Of(Sam).Select(item => item.Title).Order().ToList());
        Assert.Equal(["Cook: veggie stew", "Start the slow cooker"], scenario.Calendar.Of(Lee).Select(item => item.Title).Order().ToList());
    }

    // ---- a move is the new time ----

    [Fact]
    public async Task AMovedBlockIsHeldAtTheNewTime()
    {
        var scenario = await FirstRunAsync();
        var key = CookKey(Lee, 1);

        // The owner drags Tuesday's cook block earlier. The next run also wants a quarter of an hour at the end of the afternoon,
        // which only fits if the plan has taken the new time and no longer holds the old one.
        scenario.Calendar.OwnerMoves(key, Lee, Clock(1, 15, 45), Clock(1, 17, 45));
        var errand = Errand("tidy-a", "Tidy up", 15, 1, (17, 0), (18, 0), Lee);
        var run = await scenario.RunAsync(TheWeek.Request(errand));

        var held = Assert.IsType<EventHeld>(run.Plan!.For(key));
        Assert.Equal((Clock(1, 15, 45), Clock(1, 17, 45)), (held.Start, held.End));
        var made = Assert.Single(run.Created);
        Assert.Equal("Tidy up", made.Title);
        Assert.Equal((Clock(1, 17, 45), Clock(1, 18)), (made.Start, made.End));
    }

    [Fact]
    public async Task AMovedBlockIsNeverMovedBack()
    {
        var scenario = await FirstRunAsync();
        var key = CookKey(Sam, 0);
        scenario.Calendar.OwnerMoves(key, Sam, Clock(0, 16, 30), Clock(0, 17, 15));
        var creates = scenario.Calendar.CreateCalls;

        await scenario.RunAsync(TheWeek.Request());
        await scenario.RunAsync(TheWeek.Request());

        Assert.Equal(creates, scenario.Calendar.CreateCalls);
        var found = scenario.Calendar.By(key, Sam)!;
        Assert.Equal((Clock(0, 16, 30), Clock(0, 17, 15)), (found.Start, found.End));
    }

    [Fact]
    public async Task AMovedMorningStartIsHeldToo()
    {
        var scenario = await FirstRunAsync();
        var key = StartKey(Lee, 1);
        scenario.Calendar.OwnerMoves(key, Lee, Clock(1, 10), Clock(1, 10, 10));

        var run = await scenario.RunAsync(TheWeek.Request());

        Assert.Empty(run.Created);
        var held = Assert.IsType<EventHeld>(run.Plan!.For(key));
        Assert.Equal(Clock(1, 10), held.Start);
    }

    // ---- a delete is a skip ----

    [Fact]
    public async Task ADeletedBlockIsSkippedForGood()
    {
        var scenario = await FirstRunAsync();
        var key = CookKey(Sam, 0);
        scenario.Calendar.OwnerDeletes(key, Sam);

        var second = await scenario.RunAsync(TheWeek.Request());
        var third = await scenario.RunAsync(TheWeek.Request());

        Assert.Empty(second.Created);
        Assert.Empty(third.Created);
        Assert.IsType<EventSkipped>(third.Plan!.For(key));
        Assert.Null(scenario.Calendar.By(key, Sam));
        Assert.Empty(third.Failed);
    }

    [Fact]
    public async Task ADeletedMorningStartIsSkippedAndTheBlockStays()
    {
        var scenario = await FirstRunAsync();
        scenario.Calendar.OwnerDeletes(StartKey(Lee, 1), Lee);

        var run = await scenario.RunAsync(TheWeek.Request());

        Assert.Empty(run.Created);
        Assert.IsType<EventSkipped>(run.Plan!.For(StartKey(Lee, 1)));
        Assert.IsType<EventHeld>(run.Plan.For(CookKey(Lee, 1)));
    }

    // ---- nothing changed, nothing written ----

    [Fact]
    public async Task AReRunOnAnUnchangedWeekWritesNothing()
    {
        var scenario = await FirstRunAsync();
        var before = scenario.Calendar.Snapshot();
        var creates = scenario.Calendar.CreateCalls;
        var saves = scenario.Store.Saves;

        var run = await scenario.RunAsync(TheWeek.Request());

        Assert.Empty(run.Created);
        Assert.Empty(run.Failed);
        Assert.Equal(4, run.Plan!.Held.Count);
        Assert.Equal(creates, scenario.Calendar.CreateCalls);
        Assert.Equal(saves, scenario.Store.Saves);
        Assert.Equal(before, scenario.Calendar.Snapshot());
    }

    [Fact]
    public async Task ANewEveningIsPlacedAlone()
    {
        var scenario = await FirstRunAsync();
        var grown = new MealPlanWeek(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Stew, Lee, 120), CookedOn(2, Pasta, Sam, 30), CookedOn(4, Pasta, Lee, 30)));

        var run = await scenario.RunAsync(grown.Request());

        var made = Assert.Single(run.Created);
        Assert.Equal((Lee, "Cook: tomato pasta"), (made.Person, made.Title));
        Assert.Equal(Clock(4, 18), made.End);
    }

    // ---- what the key means ----

    [Fact]
    public async Task ADifferentDishOnTheSameEveningKeepsTheEvent()
    {
        // A meal's identity is its evening and role, so a dish changed after the event was written does not make a second event.
        var scenario = await FirstRunAsync();
        var changed = new MealPlanWeek(WeekOf(CookedOn(0, Pasta, Sam, 30), CookedOn(1, Stew, Lee, 120), CookedOn(2, Pasta, Sam, 30)));

        var run = await scenario.RunAsync(changed.Request());

        Assert.Empty(run.Created);
        Assert.Equal("Cook: chili", scenario.Calendar.By(CookKey(Sam, 0), Sam)!.Title);
    }

    [Fact]
    public async Task ANewCookGetsAnEventOfTheirOwn()
    {
        // The key names the person whose calendar it is on. The writer cannot delete, so the first cook's event stays for them to remove.
        var scenario = await FirstRunAsync();
        var swapped = new MealPlanWeek(WeekOf(CookedOn(0, Chili, Lee, 45), CookedOn(1, Stew, Lee, 120), CookedOn(2, Pasta, Sam, 30)));

        var run = await scenario.RunAsync(swapped.Request());

        var made = Assert.Single(run.Created);
        Assert.Equal((Lee, "Cook: chili"), (made.Person, made.Title));
        Assert.NotNull(scenario.Calendar.By(CookKey(Sam, 0), Sam));
    }

    /// <summary>A week of dinners and the request it makes, with whatever else is asked for after the meals.</summary>
    private sealed class MealPlanWeek(MealWeek week)
    {
        public PlacementRequest Request(params HouseholdEventRequest[] after) => RequestFor(week, options: SlowStew, after: after);
    }
}
