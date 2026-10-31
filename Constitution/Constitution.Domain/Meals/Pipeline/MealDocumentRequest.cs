using Constitution.Domain.Calendar;
using Constitution.Domain.Documents;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The children's different dinner on one evening, with the card it stands for so a child who ate it rates that dish (D37).
/// <b>No source for this exists yet</b>: the planner only says that the children eat differently, never what they eat. A run with none prints no kids' page.
/// </summary>
public sealed record KidsMealFacts(RecipeKidsMeal Meal, RecipeId Card);

/// <summary>
/// What the documents step of the weekly meal run is told: the week as it was kept, the box, the rules, and the moment on the household's clock.
/// Everything else (the roster, the pantry, the budget, the holidays, the tablet) is the step's own reads.
/// </summary>
/// <param name="Week">The planned week as it was kept: an evening already over keeps the dinner it had.</param>
/// <param name="DeliverFor">The adults whose pages this phone puts on its tablet (D85). Only the first is served: a phone has one tablet.</param>
public sealed record MealDocumentRequest(MealWeek Week, IReadOnlyList<Recipe> Cards, PlanningRules Rules, DateTimeOffset Now, IReadOnlyList<string> DeliverFor)
{
    /// <summary>The cooked dinners and the earlier weeks' planned ones the planner was shown. A card in none of them is marked NEW RECIPE.</summary>
    public IReadOnlyList<CookedMeal> History { get; init; } = [];

    /// <summary>The cards that cook in a slow cooker (the sheet marks them).</summary>
    public IReadOnlySet<RecipeId> SlowCookerCards { get; init; } = new HashSet<RecipeId>();

    /// <summary>The children's different dinner by evening. Empty when there is no source.</summary>
    public IReadOnlyDictionary<DateOnly, KidsMealFacts> KidsMeals { get; init; } = new Dictionary<DateOnly, KidsMealFacts>();

    /// <summary>What each person last said of each dish, for the rating page's underline; nobody's when there is no profile.</summary>
    public LastRatings Ratings { get; init; } = LastRatings.None;

    /// <summary>Plain shopping-list lines the sheet's groceries page adds after the aisles ("Tuesday dinner", D111); empty unless a denied evening put one there.</summary>
    public IReadOnlyList<string> ExtraLines { get; init; } = [];

    /// <summary>The breakfasts, lunches and lunchbox the rotating templates chose (D110); null leaves pages 2 and 3 off the sheet.</summary>
    public MealSheetDailyMeals? Daily { get; init; }
}
