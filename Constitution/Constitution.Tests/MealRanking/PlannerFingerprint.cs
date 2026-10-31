using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealRanking;

/// <summary>A hash of the weeks the planner makes over many invented requests, so a change that must leave a week alone can be held to it byte for byte.</summary>
internal static class PlannerFingerprint
{
    public const int Seeds = 240;

    /// <summary>The request for one seed: random cards, history, soon marks, children's evenings and never-agains, as the property tests make them.</summary>
    public static MealWeekRequest RequestFor(int seed)
    {
        var random = new Random(seed);
        var cards = Enumerable.Range(0, random.Next(10, 19))
            .Select(index => Dish("dish-" + index, Cuisines.All[random.Next(Cuisines.All.Count)], forKids: random.Next(3) == 0, minutes: random.Next(5, 200)))
            .ToList();
        var history = Enumerable.Range(0, random.Next(0, 22))
            .Select(_ => new CookedMeal(cards[random.Next(cards.Count)].Id, Sunday.AddDays(-random.Next(1, 70))))
            .ToList();
        var soon = cards.Where(_ => random.Next(6) == 0).Select(card => card.Id);
        var children = Enumerable.Range(0, 7).Where(_ => random.Next(2) == 0).Select(offset => Sunday.AddDays(offset)).ToHashSet();
        var vetoes = cards.Where(_ => random.Next(8) == 0).ToDictionary(card => card.Id, _ => (IReadOnlySet<string>)new HashSet<string> { random.Next(3) switch { 0 => Sam, 1 => Lee, _ => Robin } });
        return Request(null, null, null, cards, children, soon, history, vetoes, null, seed);
    }

    /// <summary>FNV-1a over every seed's stored week, in seed order.</summary>
    public static ulong Of(Func<int, MealWeekRequest> requestFor)
    {
        var hash = 14695981039346656037UL;
        for (var seed = 0; seed < Seeds; seed++)
        {
            foreach (var value in System.Text.Encoding.UTF8.GetBytes(seed + ":" + MealWeekCodec.Write(MealPlanner.Plan(requestFor(seed)))))
            {
                hash = (hash ^ value) * 1099511628211UL;
            }
        }

        return hash;
    }
}
