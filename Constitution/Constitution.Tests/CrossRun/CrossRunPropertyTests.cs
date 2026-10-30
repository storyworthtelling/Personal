using Xunit.Abstractions;

namespace Constitution.Tests.CrossRun;

/// <summary>
/// R7 across the two runs (the dealbreaker: an event placed on top of someone's life). The workout run and the meal run write to the same
/// calendars through the same engine and the same records, and each read used to drop every event that carried the app's key, so neither
/// could see the other's cook block, slow-cooker start, Groceries trip or workout. Seeded invented fortnights are placed in both orders,
/// through the real calendar source, the real engine and runner, and a fake calendar that keeps what was created:
/// no workout overlaps a meal event on the same calendar, nothing lands on the owner's own life, and a second pass over the unchanged
/// fortnight writes nothing. A control run with the blind read proves the property can fail.
/// </summary>
public class CrossRunPropertyTests(ITestOutputHelper output)
{
    private const int Rounds = 120;
    private const int FirstSeed = 41000;

    private sealed record Totals(int Worlds, int Meals, int Workouts, int Crossed, int OnLife, int Unquiet, int Refused);

    private static async Task<Totals> RunSeedsAsync(bool blind)
    {
        var worlds = 0;
        var meals = 0;
        var workouts = 0;
        var crossed = 0;
        var onLife = 0;
        var unquiet = 0;
        var refused = 0;
        for (var round = 0; round < Rounds; round++)
        {
            var seed = FirstSeed + round;
            if (CrossRunScenario.Generate(seed) is not { } scenario)
            {
                continue;
            }

            foreach (var mealsFirst in new[] { true, false })
            {
                var world = new CrossRunWorld(scenario, blind);
                var first = await world.RunAllAsync(mealsFirst);
                var reading = world.Read();
                var before = world.Fingerprint();
                var again = await world.RunAllAsync(mealsFirst);

                worlds++;
                meals += reading.Meals;
                workouts += reading.Workouts;
                crossed += reading.WorkoutsOnMeals;
                onLife += reading.OnTheOwnersLife;
                refused += first.Sum(run => run.Plan?.Unplaced.Count ?? 0);
                if (world.Fingerprint() != before || again.Any(run => run.Created.Count > 0))
                {
                    unquiet++;
                }

                Assert.All(first.Concat(again), run => Assert.True(run.BlockedBecause is null && run.Failed.Count == 0, $"A run was blocked or refused by the calendar: seed {seed}, meals first {mealsFirst}."));
                if (!blind)
                {
                    Assert.True(reading.WorkoutsOnMeals == 0, $"A workout overlaps a meal event: seed {seed}, meals first {mealsFirst}.");
                    Assert.True(reading.OnTheOwnersLife == 0, $"An event lands on the owner's own calendar entry: seed {seed}, meals first {mealsFirst}.");
                    Assert.True(world.Fingerprint() == before && again.All(run => run.Created.Count == 0), $"A second pass over the unchanged fortnight wrote something: seed {seed}, meals first {mealsFirst}.");
                }
            }
        }

        return new Totals(worlds, meals, workouts, crossed, onLife, unquiet, refused);
    }

    [Fact]
    public async Task NeitherRunLandsOnTheOther()
    {
        var totals = await RunSeedsAsync(blind: false);
        output.WriteLine($"family read: {totals}");

        // Not vacuous: plenty of both runs' events were placed, in many invented fortnights, and some were refused for want of room.
        Assert.True(totals.Worlds > 150, $"Only {totals.Worlds} fortnights were placed.");
        Assert.True(totals.Meals > 600, $"Only {totals.Meals} meal events were placed.");
        Assert.True(totals.Workouts > 600, $"Only {totals.Workouts} workouts were placed.");
        Assert.True(totals.Refused > 0, "Nothing was ever refused, so the crowded half never bit.");
        Assert.Equal(0, totals.Crossed);
        Assert.Equal(0, totals.OnLife);
        Assert.Equal(0, totals.Unquiet);
    }

    [Fact]
    public async Task TheBlindReadIsCaughtByTheSameFortnights()
    {
        var totals = await RunSeedsAsync(blind: true);
        output.WriteLine($"blind read: {totals}");

        // The control: with each run dropping every keyed event, as the code did before the families, the same fortnights put workouts on
        // meals, so the property above has something to fail on.
        Assert.True(totals.Crossed > 50, $"The blind read crossed only {totals.Crossed} times; the property above may be proving nothing.");
    }
}
