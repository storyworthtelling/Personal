using Constitution.Domain.Calendar;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>The arithmetic the planner and the rules both read, in one place so they cannot disagree about an evening.</summary>
internal static class MealFacts
{
    public static readonly TimeSpan ShortestCook = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan LongestCook = TimeSpan.FromMinutes(120);

    public static (DateTimeOffset Start, DateTimeOffset End) DinnerWindow(PlanningRules rules, DateOnly day) =>
        ZoneClock.Window(day, rules.Dinner.Start, rules.Dinner.Length, rules.Zone);

    public static BusyTime? TimeOf(MealWeekRequest request, string name) =>
        request.AdultTime.FirstOrDefault(time => string.Equals(time.Person, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Home at dinner: nothing on a calendar (theirs, the family's, a child's event they are on the hook for) overlaps the dinner window.</summary>
    public static bool IsHome(BusyTime time, DateTimeOffset dinnerStart, DateTimeOffset dinnerEnd) =>
        !time.Busy.Any(interval => interval.Overlaps(dinnerStart, dinnerEnd));

    /// <summary>The block a card asks for: all its timed steps, hands-on or waiting, held to 30 to 120 minutes.</summary>
    public static TimeSpan CookLength(Recipe recipe)
    {
        var minutes = recipe.Steps.Where(step => step.Minutes is not null).Sum(step => step.Minutes!.Value);
        var length = TimeSpan.FromMinutes(minutes);
        return length < ShortestCook ? ShortestCook : length > LongestCook ? LongestCook : length;
    }

    public static bool RepeatsWithin(RecipeId id, DateOnly day, IEnumerable<CookedMeal> meals) =>
        meals.Any(meal => meal.Recipe == id && Math.Abs(meal.Day.DayNumber - day.DayNumber) < MealPlanner.RepeatDays);

    public static DateOnly LastCooked(RecipeId id, IEnumerable<CookedMeal> history)
    {
        var days = history.Where(meal => meal.Recipe == id).Select(meal => meal.Day).ToList();
        return days.Count == 0 ? DateOnly.MinValue : days.Max();
    }

    /// <summary>A stable tie-break: FNV-1a over the seed and two words. string.GetHashCode is different every run, so it cannot be used.</summary>
    public static uint Hash(int seed, string first, string second)
    {
        var hash = 2166136261u;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(seed + "|" + first + "|" + second))
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }
}
