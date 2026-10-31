using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What the weekly meal sheet is made from.</summary>
/// <param name="Headcount">Who is at the table, for the groceries.</param>
public sealed record MealSheetBuildRequest(
    MealWeek Week,
    IReadOnlyList<Recipe> Cards,
    TimeZoneInfo Zone,
    int Headcount,
    Pantry Pantry,
    MonthlyBudgets Budgets,
    IHolidayCalendar Holidays)
{
    public IReadOnlyList<CookedMeal> History { get; init; } = [];

    public IReadOnlySet<RecipeId> SlowCookerCards { get; init; } = new HashSet<RecipeId>();

    public IReadOnlyDictionary<DateOnly, KidsMealFacts> KidsMeals { get; init; } = new Dictionary<DateOnly, KidsMealFacts>();

    /// <summary>Plain lines for the shopping list that are not ingredients ("Tuesday dinner", D111); empty unless a denied evening put one there.</summary>
    public IReadOnlyList<string> ExtraLines { get; init; } = [];

    /// <summary>The breakfasts, lunches and lunchbox the rotating templates chose (D110); null leaves pages 2 and 3 out.</summary>
    public MealSheetDailyMeals? Daily { get; init; }
}

/// <summary>
/// Builds the model <see cref="MealSheetDocument"/> draws from a planned week. Pure.
///
/// <para><b>What is on the sheet.</b> The dinners (page 1), the groceries with the standing staples and the budget in words, and the holiday ask-ahead when
/// a holiday the household hosts or visits falls in the week after. <see cref="MealSheetModel.Daily"/> is whatever the rotating templates chose
/// (<see cref="MealSheetBuildRequest.Daily"/>, D110) and is left null without it: the planner plans dinners only, so with no approved or proposed template pages
/// 2 and 3 are not printed rather than printed with words nobody chose.</para>
///
/// <para><b>The page count is the pages built.</b> <see cref="MealSheetModel.Pages"/> is dinners, plus the groceries page, plus the two daily-meals pages when there are daily meals, plus the ask page when there is one;
/// a long grocery list that runs onto more pages is counted by the document itself, so every footer's "x of N" is the sheet's real length.</para>
///
/// <para><b>The markers are facts, not decoration.</b> SLOW COOKER only for a card the caller says cooks in one. NEW RECIPE only for a card that appears in no
/// cooked or planned history, and only when there is some history to speak of: with none, nothing is known, so nothing is claimed new.</para>
/// </summary>
public static class MealSheetBuilder
{
    /// <summary>The dinners page and the groceries page; the ask-ahead page, when there is one, is one more.</summary>
    public const int BasePages = 2;

    /// <summary>The breakfast and lunch page and the lunchbox page, which a sheet with daily meals adds.</summary>
    public const int DailyPages = 2;

    public static MealSheetModel Build(MealSheetBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var week = request.Week;
        if (!week.IsPlanned)
        {
            throw new ArgumentException("A week that was not planned has no sheet to print: " + week.StopReason, nameof(request));
        }

        var known = request.History.Select(meal => meal.Recipe).ToHashSet();
        var dishes = new Dictionary<RecipeId, MealSheetDish>();
        foreach (var dinner in week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
        {
            var id = dinner.Recipe!.Value;
            var card = request.Cards.FirstOrDefault(item => item.Id == id)
                ?? throw new ArgumentException($"{dinner.Day.DayOfWeek}'s dinner names a card that is not in the box, so the sheet cannot name it.", nameof(request));
            var kids = dinner.ChildrenEatDifferently && request.KidsMeals.TryGetValue(dinner.Day, out var facts) ? facts.Meal.Title : null;
            var marker = request.SlowCookerCards.Contains(id)
                ? MealMarker.SlowCooker
                : request.History.Count > 0 && !known.Contains(id) ? MealMarker.NewRecipe : (MealMarker?)null;
            dishes[id] = dishes.TryGetValue(id, out var already)
                ? already with { KidsMeal = already.KidsMeal ?? kids }
                : new MealSheetDish(card.Title, marker, kids);
        }

        var list = GroceryList.From(week, request.Cards, request.Pantry, request.Headcount, request.ExtraLines);
        var budget = GroceryBudget.Read(request.Budgets, GroceryBudget.MonthOf(week.WeekStart));
        var ask = HolidayAskAhead.Decide(request.Holidays, week.WeekStart);
        return new MealSheetModel(
            week,
            dishes,
            MealDeadline.ForWeek(week.WeekStart, request.Zone),
            Pages: BasePages + (request.Daily is null ? 0 : DailyPages) + (ask is null ? 0 : 1),
            Daily: request.Daily,
            Groceries: new MealSheetGroceries(list, request.Pantry, budget),
            AskAhead: ask);
    }
}
