using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealPlanning;

/// <summary>
/// A few hundred seeded invented weeks (random calendars, random history, random flags): whatever the week looks like, no cook
/// is ever on blocked time, nothing repeats within 14 days, and the rules planner's own week breaks no rule. A failure prints the
/// seed and round.
/// </summary>
public class MealWeekPropertyTests
{
    private const int Rounds = 300;
    private const int FirstSeed = 6100;

    private static readonly string[] Titles = ["Dentist", "Call", "Plumber", "Recital", "Haircut", "Class", "Robin soccer, Lee drives", "Juniper swimming"];

    private static List<CalendarEntry> RandomEntries(Random random)
    {
        var entries = new List<CalendarEntry>();
        var count = random.Next(0, 9);
        for (var index = 0; index < count; index++)
        {
            var day = Sunday.AddDays(random.Next(0, 7));
            var from = random.Next(14, 21);
            var to = Math.Min(23, from + random.Next(1, 4));
            var minute = random.Next(0, 2) * 30;
            var local = new DateTimeOffset(new DateTime(day.Year, day.Month, day.Day, from, minute, 0), Zone.GetUtcOffset(new DateTime(day.Year, day.Month, day.Day, from, minute, 0)));
            var end = local.AddHours(to - from);
            entries.Add(CalendarEntry.Timed(Titles[random.Next(Titles.Length)], local, end));
        }

        return entries;
    }

    [Fact]
    public void RandomWeeksKeepEveryRule()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = FirstSeed + round;
            var random = new Random(seed);
            var cards = Enumerable.Range(0, random.Next(10, 19))
                .Select(index => Dish("dish-" + index, Cuisines.All[random.Next(Cuisines.All.Count)], forKids: random.Next(3) == 0, minutes: random.Next(5, 200)))
                .ToList();
            var history = Enumerable.Range(0, random.Next(0, 22))
                .Select(_ => new CookedMeal(cards[random.Next(cards.Count)].Id, Sunday.AddDays(-random.Next(1, 40))))
                .ToList();
            var soon = cards.Where(_ => random.Next(6) == 0).Select(card => card.Id);
            var children = Enumerable.Range(0, 7).Where(_ => random.Next(2) == 0).Select(offset => Sunday.AddDays(offset)).ToHashSet();
            var vetoes = cards.Where(_ => random.Next(8) == 0).ToDictionary(card => card.Id, _ => (IReadOnlySet<string>)new HashSet<string> { random.Next(3) switch { 0 => Sam, 1 => Lee, _ => Robin } });
            var request = Request(RandomEntries(random), RandomEntries(random), RandomEntries(random), cards, children, soon, history, vetoes, null, seed);

            var week = MealPlanner.Plan(request);
            var where = $"seed {seed}, round {round}";

            Assert.True(week.IsPlanned, where);
            Assert.Empty(MealRules.Violations(request, week));
            foreach (var dinner in Cooks(week))
            {
                var time = request.AdultTime.Single(t => t.Person == dinner.Cook);
                Assert.True(time.IsFree(dinner.CookStart!.Value, dinner.CookEnd!.Value), where + ": cook on blocked time on " + dinner.Day);
                var all = history.Where(meal => meal.Recipe == dinner.Recipe).Select(meal => meal.Day)
                    .Concat(Cooks(week).Where(other => other != dinner && other.Recipe == dinner.Recipe).Select(other => other.Day));
                Assert.All(all, day => Assert.True(Math.Abs(day.DayNumber - dinner.Day.DayNumber) >= MealPlanner.RepeatDays, where + ": repeat on " + dinner.Day));
            }

            Assert.Equal(MealWeekCodec.Write(week), MealWeekCodec.Write(MealPlanner.Plan(request)));
            Assert.True(Cooks(week).Count(dinner => dinner.IsStretch) <= 1, where);
        }
    }
}
