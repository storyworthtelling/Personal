using Constitution.Domain.Calendar;
using Constitution.Domain.Groceries;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// What one run of the weekly meal run is asked: which week, when it is, and by what rules. The recipes, the history, the taste
/// profile and the calendars are the run's own reads (its seams), not inputs.
/// </summary>
/// <param name="WeekStart">The Sunday of the week being planned (<see cref="PlanningWeek"/>); any other day is refused.</param>
/// <param name="Now">The moment of the run on the household's clock. Nothing reads another clock.</param>
/// <param name="Rules">Who the adults are, the zone, and when the family dinner is.</param>
public sealed record MealWeekRequestInputs(DateOnly WeekStart, DateTimeOffset Now, PlanningRules Rules)
{
    /// <summary>Breaks ties in the planner. Null means the week's own number, so every run on the same week breaks them the same way.</summary>
    public int? Seed { get; init; }

    /// <summary>The evenings the children eat at home. <b>No source for this exists yet</b>: the head leaves it empty, which plans every evening for adults only.</summary>
    public IReadOnlySet<DateOnly> ChildrenHome { get; init; } = new HashSet<DateOnly>();

    /// <summary>Cards marked Cook this soon. <b>No store for the mark exists yet</b>: the head leaves it empty.</summary>
    public IReadOnlySet<RecipeId> CookThisSoon { get; init; } = new HashSet<RecipeId>();

    /// <summary>When the week was approved, for the Groceries trip (never before it). Null means <see cref="Now"/>: until there is an approval step, a run is the approval.</summary>
    public DateTimeOffset? ApprovedAt { get; init; }

    public MealPlacementOptions Placement { get; init; } = new();

    public GroceryTripOptions Groceries { get; init; } = new();

    /// <summary>
    /// The adults whose pages this phone puts on its tablet (each phone is paired to its own tablet, D85). Empty means this phone has no tablet to
    /// deliver to, and the documents step says so rather than guessing.
    /// </summary>
    public IReadOnlyList<string> DeliverFor { get; init; } = [];

    /// <summary>The children's different dinner by evening. <b>No source exists yet</b>: the head leaves it empty, which prints no kids' page.</summary>
    public IReadOnlyDictionary<DateOnly, KidsMealFacts> KidsMeals { get; init; } = new Dictionary<DateOnly, KidsMealFacts>();
}
