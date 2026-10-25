using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using Constitution.Tests.MealPlanning;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// C-5.3.4 for meals (R7, the dealbreaker): two hundred seeded invented weeks. Each week is planned on one set of calendars and then
/// placed on a second set that has had more put on it since, so the engine is asked to place meals the planner never saw the
/// clash for. Whatever the calendars look like: no meal event overlaps blocked time, no two events of one person overlap, every
/// block ends at dinner, nothing asked for goes missing without a reason, and a re-run writes nothing. A failure prints the seed
/// and round.
/// </summary>
public class MealPlacementPropertyTests
{
    private const int Rounds = 200;
    private const int FirstSeed = 8800;

    private static readonly string[] Titles = ["Dentist", "Call", "Plumber", "Recital", "Haircut", "Class", "Robin soccer, Lee drives", "Juniper swimming"];

    private static List<CalendarEntry> RandomEntries(Random random, int most)
    {
        var entries = new List<CalendarEntry>();
        var count = random.Next(0, most);
        for (var index = 0; index < count; index++)
        {
            var day = Sunday.AddDays(random.Next(0, 7));
            var from = random.Next(5, 21);
            var to = Math.Min(23, from + random.Next(1, 4));
            var minute = random.Next(0, 2) * 30;
            var local = new DateTime(day.Year, day.Month, day.Day, from, minute, 0);
            var start = new DateTimeOffset(local, Zone.GetUtcOffset(local));
            entries.Add(CalendarEntry.Timed(Titles[random.Next(Titles.Length)], start, start.AddHours(to - from)));
        }

        return entries;
    }

    [Fact]
    public async Task RandomWeeksNeverBreakACalendar()
    {
        var created = 0;
        var refused = 0;
        var slowStarts = 0;
        for (var round = 0; round < Rounds; round++)
        {
            var seed = FirstSeed + round;
            var random = new Random(seed);
            var where = $"seed {seed}, round {round}";
            var cards = Enumerable.Range(0, random.Next(10, 19))
                .Select(index => MealData.Dish("dish-" + index, Cuisines.All[random.Next(Cuisines.All.Count)], forKids: random.Next(3) == 0, minutes: random.Next(5, 200)))
                .ToList();
            var sam = RandomEntries(random, 7);
            var lee = RandomEntries(random, 7);
            var family = RandomEntries(random, 5);
            var week = MealPlanner.Plan(MealData.Request(sam, lee, family, cards, seed: seed));
            if (!week.IsPlanned)
            {
                continue;
            }

            // What has been put on the calendars since the week was planned.
            var rules = Rules();
            var later = Calendars([.. sam, .. RandomEntries(random, 4)], [.. lee, .. RandomEntries(random, 4)], [.. family, .. RandomEntries(random, 3)]);
            var people = MealData.AdultTime(rules, later);
            var options = new MealPlacementOptions { SlowCookerCards = cards.Where(_ => random.Next(3) == 0).Select(card => card.Id).ToHashSet() };
            var request = MealCalendar.Request(week, rules, cards, people, null, options);
            var wanted = PlacementEngine.Wanted(request);

            var scenario = new Scenario();
            var first = await scenario.RunAsync(request);

            Assert.True(first.BlockedBecause is null, where);
            Assert.True(first.Failed.Count == 0, where);
            Assert.True(first.Created.Count + first.Plan!.Unplaced.Count == wanted.Count, $"Something asked for went missing without a reason: {where}");
            Assert.All(first.Plan.Unplaced, item => Assert.True(item.Reason == UnplacedReason.NoRoom, where));
            created += first.Created.Count;
            refused += first.Plan.Unplaced.Count;

            foreach (var made in first.Created)
            {
                var time = people.Single(person => string.Equals(person.Person, made.Person, StringComparison.OrdinalIgnoreCase));
                Assert.True(time.Blocked.All(interval => !interval.Overlaps(made.Start, made.End)), $"{made.Title} lands on blocked time: {where}");
                var day = ZoneClock.DayOf(made.Start, rules.Zone);
                var dinner = ZoneClock.Window(day, rules.Dinner.Start, rules.Dinner.Length, rules.Zone);
                if (made.Title.StartsWith("Cook: ", StringComparison.Ordinal))
                {
                    var choice = week.Dinners.Single(item => item.Day == day);
                    Assert.True(made.End == dinner.Start, $"A block does not end at dinner: {where}");
                    Assert.True(made.Start == choice.CookStart && made.End == choice.CookEnd, $"A block is not the planned one: {where}");
                    Assert.True(string.Equals(made.Person, choice.Cook, StringComparison.OrdinalIgnoreCase), $"A block is on the wrong calendar: {where}");
                }
                else
                {
                    slowStarts++;
                    Assert.Equal("Start the slow cooker", made.Title);
                    Assert.True(made.End <= dinner.Start, $"A start runs into dinner: {where}");
                }

                Assert.True(!Overlap(made.Start, made.End, dinner.Start, dinner.End), $"{made.Title} overlaps the family dinner: {where}");
            }

            foreach (var person in first.Created.GroupBy(item => item.Person))
            {
                var mine = person.OrderBy(item => item.Start).ToList();
                for (var index = 1; index < mine.Count; index++)
                {
                    Assert.True(mine[index - 1].End <= mine[index].Start, $"Two events of {person.Key} overlap: {where}");
                }
            }

            var snapshot = scenario.Calendar.Snapshot();
            var creates = scenario.Calendar.CreateCalls;
            var again = await scenario.RunAsync(request);
            Assert.True(again.Created.Count == 0, $"A re-run wrote something: {where}");
            Assert.True(scenario.Calendar.CreateCalls == creates && scenario.Calendar.Snapshot() == snapshot, $"A re-run changed the calendar: {where}");
        }

        // The property is not vacuous: plenty was placed, the late clashes did refuse some, and morning starts were made.
        Assert.True(created > 400, $"Only {created} events were placed over {Rounds} weeks.");
        Assert.True(refused > 0, "No late clash ever refused a meal, so the adversarial half never bit.");
        Assert.True(slowStarts > 20, $"Only {slowStarts} morning starts were placed.");
    }
}
