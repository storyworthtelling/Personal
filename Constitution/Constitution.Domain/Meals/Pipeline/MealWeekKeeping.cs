using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// What a re-run does to a week already saved. An evening that is over is history: a re-plan on a later day of the same week sees
/// "that evening has already passed" and would write it over, so the saved dinner for any day before today is kept, and so is the first
/// moment the week was planned.
/// </summary>
public static class MealWeekKeeping
{
    public static StoredMealWeek Keep(StoredMealWeek? previous, StoredMealWeek fresh, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        if (previous is null)
        {
            return fresh;
        }

        if (previous.WeekStart != fresh.WeekStart)
        {
            throw new ArgumentException("A saved week is only kept against another copy of the same week.", nameof(previous));
        }

        var dinners = fresh.Week.Dinners
            .Select(dinner => dinner.Day < today && previous.Week.Dinners.FirstOrDefault(old => old.Day == dinner.Day) is { } over ? over : dinner)
            .ToList();
        var named = dinners.Where(dinner => dinner.Recipe is not null).Select(dinner => dinner.Recipe!.Value).ToHashSet();
        var dishes = fresh.Dishes
            .Concat(previous.Dishes.Where(old => fresh.Dishes.All(dish => dish.Recipe != old.Recipe)))
            .Where(dish => named.Contains(dish.Recipe))
            .ToList();
        return new StoredMealWeek(MealWeek.Planned(fresh.WeekStart, dinners, fresh.Week.Notes), previous.PlannedAt, dishes);
    }
}

/// <summary>
/// The planner's memory of weeks it planned itself, taken with no answers to go by: every cooked dinner of an earlier week stands in as planned, kept out of
/// the cooked history on purpose and never written into the history file as if it had happened. The run itself uses <see cref="CookedHistory"/>, which is this
/// plus what the household answered (D130).
/// </summary>
public static class PlannedHistory
{
    /// <summary>The cooked history plus every cooked dinner of a saved week that began before <paramref name="weekStart"/>, one entry per card and evening.</summary>
    public static IReadOnlyList<CookedMeal> Merge(IReadOnlyList<CookedMeal> cooked, MealWeekRecord? record, DateOnly weekStart) =>
        CookedHistory.Build(cooked, record, [], [], weekStart).Meals;
}
