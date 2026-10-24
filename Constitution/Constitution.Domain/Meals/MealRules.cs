namespace Constitution.Domain.Meals;

/// <summary>Which rule a week breaks.</summary>
public enum MealRule
{
    /// <summary>Not seven evenings from the Monday, in order, or not planned at all.</summary>
    WrongEvenings,

    /// <summary>A cooked dinner names a card that is not in the box.</summary>
    NotInTheBox,

    /// <summary>A card cooked again within <see cref="MealPlanner.RepeatDays"/> days of itself (in the history or the same week).</summary>
    RepeatsWithinFourteenDays,

    /// <summary>The cook block overlaps time the cook's calendar or the household's rules keep clear.</summary>
    CookBlockedTime,

    /// <summary>The cook is not one of the household's adults, or is not home that evening.</summary>
    CookNotAvailable,

    /// <summary>The block does not end where the family dinner starts.</summary>
    CookBlockMisplaced,

    /// <summary>A card someone said "Never again" to, on an evening that person eats it.</summary>
    VetoedDish,

    /// <summary>The children are at the table, the dish is not one of theirs, and the dinner does not give them a different meal.</summary>
    ChildrenNotCatered,
}

public sealed record MealViolation(DateOnly Day, MealRule Rule);

/// <summary>The rules every dinner week is held to, whoever wrote it. The rules planner's own week must come back empty.</summary>
public static class MealRules
{
    public static IReadOnlyList<MealViolation> Violations(MealWeekRequest request, MealWeek week)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(week);
        if (!week.IsPlanned || week.WeekStart != request.WeekStart || week.Dinners.Count != MealWeek.Evenings)
        {
            return [new MealViolation(request.WeekStart, MealRule.WrongEvenings)];
        }

        var found = new List<MealViolation>();
        var earlier = new List<CookedMeal>();
        foreach (var dinner in week.Dinners)
        {
            if (dinner.Kind != DinnerKind.Cooked)
            {
                continue;
            }

            Check(request, dinner, earlier, found);
            earlier.Add(new CookedMeal(dinner.Recipe!.Value, dinner.Day));
        }

        return found;
    }

    private static void Check(MealWeekRequest request, DinnerChoice dinner, List<CookedMeal> earlier, List<MealViolation> found)
    {
        void Add(MealRule rule) => found.Add(new MealViolation(dinner.Day, rule));

        var recipe = request.Recipes.FirstOrDefault(card => card.Id == dinner.Recipe);
        if (recipe is null)
        {
            Add(MealRule.NotInTheBox);
        }
        else if (dinner.Recipe is { } id && MealFacts.RepeatsWithin(id, dinner.Day, request.History.Concat(earlier)))
        {
            Add(MealRule.RepeatsWithinFourteenDays);
        }

        var (dinnerStart, dinnerEnd) = MealFacts.DinnerWindow(request.Rules, dinner.Day);
        var adult = request.Rules.FindAdult(dinner.Cook!);
        var time = adult is null ? null : MealFacts.TimeOf(request, adult.Name);
        if (adult is null || time is null || !MealFacts.IsHome(time, dinnerStart, dinnerEnd))
        {
            Add(MealRule.CookNotAvailable);
        }

        if (dinner.CookEnd != dinnerStart)
        {
            Add(MealRule.CookBlockMisplaced);
        }

        if (time is not null && !time.IsFree(dinner.CookStart!.Value, dinner.CookEnd!.Value))
        {
            Add(MealRule.CookBlockedTime);
        }

        var childrenHome = request.ChildrenHome.Contains(dinner.Day);
        var homeNames = request.Rules.Adults
            .Where(person => MealFacts.TimeOf(request, person.Name) is { } t && MealFacts.IsHome(t, dinnerStart, dinnerEnd))
            .Select(person => person.Name)
            .ToList();
        var childVeto = false;
        if (dinner.Recipe is { } recipeId && request.NeverAgain.TryGetValue(recipeId, out var people))
        {
            foreach (var name in people)
            {
                var vetoer = request.Rules.FindAdult(name);
                if (vetoer is not null)
                {
                    if (homeNames.Contains(vetoer.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Add(MealRule.VetoedDish);
                    }
                }
                else if (childrenHome)
                {
                    childVeto = true;
                }
            }
        }

        if (childVeto && !dinner.ChildrenEatDifferently)
        {
            Add(MealRule.VetoedDish);
        }

        if (recipe is not null && childrenHome && !recipe.ForKids && !dinner.ChildrenEatDifferently)
        {
            Add(MealRule.ChildrenNotCatered);
        }
    }
}
