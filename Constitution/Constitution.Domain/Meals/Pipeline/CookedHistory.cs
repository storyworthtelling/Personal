using Constitution.Domain.Documents;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What the planner is shown of the past, and where each part came from.</summary>
/// <param name="Meals">The dinners, confirmed ones first and then the earlier weeks' in week and evening order, one entry per card and evening.</param>
/// <param name="FromAnswers">Planned dinners an adult ticked and nobody denied.</param>
/// <param name="FromPlans">Planned dinners nobody answered for: the stand-in, shown to the planner and never stored as fact.</param>
/// <param name="Dropped">Planned dinners left out because somebody denied them or an "I ate something else" took the evening.</param>
public sealed record CookedHistoryRead(IReadOnlyList<CookedMeal> Meals, int FromAnswers, int FromPlans, int Dropped);

/// <summary>
/// The memory the planner's "no repeat in 14 days" rule reads, built from what happened (D130). Pure: the weeks, the answers and the estimates are passed in, and so is
/// the Sunday of the week being planned, so the same inputs are the same history.
///
/// <para><b>Nothing is invented.</b> A dish is in the history only if it was confirmed cooked, or it is a cooked dinner of a saved week before this one that nobody
/// said did not happen. A dinner is left out when any adult denied its line on the sheet, or when an "I ate something else" was recorded for that evening's dinner; a
/// dinner with a tick stands because it was approved; one nobody answered for (no answer, or an answer that is still <see cref="MealVerdict.Unchanged"/>) stands in as
/// planned, which is the only thing known of it. Leftovers, evenings out and empty evenings are never a cooked dish. A confirmed dinner is a fact and is never taken out.</para>
///
/// <para>The conservative failure: with no answers or no estimates to read the history is the planned weeks' cooked dinners exactly as before, which can only make the
/// planner avoid a dish it might have repeated, never serve one it should have avoided.</para>
/// </summary>
public static class CookedHistory
{
    public static CookedHistoryRead Build(
        IReadOnlyList<CookedMeal> confirmed,
        MealWeekRecord? record,
        IReadOnlyList<MealAnswer> answers,
        IReadOnlyList<AteEstimate> ate,
        DateOnly weekStart)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(ate);
        var meals = confirmed.ToList();
        var seen = meals.Select(meal => (meal.Recipe, meal.Day)).ToHashSet();
        var fromAnswers = 0;
        var fromPlans = 0;
        var dropped = 0;
        foreach (var week in record?.Weeks.Where(item => item.WeekStart < weekStart) ?? [])
        {
            foreach (var dinner in week.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
            {
                var dish = dinner.Recipe!.Value;
                if (seen.Contains((dish, dinner.Day)))
                {
                    continue;
                }

                var line = MealSheetIds.DinnerYes(dinner.Day.DayOfWeek);
                var said = answers.Where(answer => answer.Week == week.WeekStart && answer.ItemId == line).ToList();
                var replaced = ate.Any(estimate => estimate.Day == dinner.Day && estimate.Meal == AteMeal.Dinner);
                if (replaced || said.Any(answer => answer.Verdict == MealVerdict.Denied))
                {
                    dropped++;
                    continue;
                }

                seen.Add((dish, dinner.Day));
                meals.Add(new CookedMeal(dish, dinner.Day));
                if (said.Any(answer => answer.Verdict == MealVerdict.Approved))
                {
                    fromAnswers++;
                }
                else
                {
                    fromPlans++;
                }
            }
        }

        return new CookedHistoryRead(meals, fromAnswers, fromPlans, dropped);
    }
}
