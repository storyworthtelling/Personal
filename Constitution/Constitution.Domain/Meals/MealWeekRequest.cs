using Constitution.Domain.Calendar;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>
/// Everything the dinner planner reads, all passed in: no clock, no storage, no calendar fetch (the head fills it).
/// A week is 7 evenings from the Sunday the planning week opens on (<see cref="PlanningWeek"/>), Sunday through Saturday.
/// </summary>
public sealed record MealWeekRequest
{
    public MealWeekRequest(
        DateOnly weekStart,
        IReadOnlyList<Recipe> recipes,
        PlanningRules rules,
        IReadOnlyList<BusyTime> adultTime,
        IReadOnlySet<DateOnly>? childrenHome = null,
        IReadOnlySet<RecipeId>? cookThisSoon = null,
        IReadOnlyList<CookedMeal>? history = null,
        IReadOnlyDictionary<RecipeId, IReadOnlySet<string>>? neverAgain = null,
        decimal? budgetSoFar = null,
        int seed = 0,
        DateTimeOffset? now = null)
    {
        WeekStart = weekStart;
        Recipes = recipes;
        Rules = rules;
        AdultTime = adultTime;
        ChildrenHome = childrenHome ?? new HashSet<DateOnly>();
        CookThisSoon = cookThisSoon ?? new HashSet<RecipeId>();
        History = history ?? [];
        NeverAgain = neverAgain ?? new Dictionary<RecipeId, IReadOnlySet<string>>();
        BudgetSoFar = budgetSoFar;
        Seed = seed;
        Now = now ?? DateTimeOffset.MinValue;
        EnsureValid();
    }

    /// <summary>The Sunday the week starts on.</summary>
    public DateOnly WeekStart { get; init; }

    /// <summary>Every card in the box.</summary>
    public IReadOnlyList<Recipe> Recipes { get; init; }

    public PlanningRules Rules { get; init; }

    /// <summary>One busy-time answer per adult, each covering the whole week. Who is home each evening and who may cook come from these.</summary>
    public IReadOnlyList<BusyTime> AdultTime { get; init; }

    /// <summary>The evenings the children eat at home. The children are one group here.</summary>
    public IReadOnlySet<DateOnly> ChildrenHome { get; init; }

    /// <summary>Cards marked Cook this soon (a plain set of ids).</summary>
    public IReadOnlySet<RecipeId> CookThisSoon { get; init; }

    /// <summary>What was cooked lately, by card and evening. The planner reads only what bears on a repeat and on the stretch dish.</summary>
    public IReadOnlyList<CookedMeal> History { get; init; }

    /// <summary>
    /// "Never again": a card and the people who said it, by name (an adult's name as the rules know it; any other name is a child).
    /// A seam for the taste profile (C-6.3.2); the rules planner honours it as the PBI words it.
    /// </summary>
    public IReadOnlyDictionary<RecipeId, IReadOnlySet<string>> NeverAgain { get; init; }

    /// <summary>The week's grocery spend so far. A seam for the budget (C-6.4.3): nothing reads it yet, and none is the usual case.</summary>
    public decimal? BudgetSoFar { get; init; }

    /// <summary>Breaks ties. The same request with the same seed is the same week.</summary>
    public int Seed { get; init; }

    /// <summary>The clock, passed in: evenings before its local day are not planned. <see cref="DateTimeOffset.MinValue"/> means nothing has passed.</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong. Run by the constructor and again by the planner (a <c>with</c> skips the constructor).</summary>
    public void EnsureValid()
    {
        PlanningWeek.RequireStart(WeekStart, nameof(WeekStart));
        ArgumentNullException.ThrowIfNull(Recipes);
        ArgumentNullException.ThrowIfNull(Rules);
        ArgumentNullException.ThrowIfNull(AdultTime);
        ArgumentNullException.ThrowIfNull(ChildrenHome);
        ArgumentNullException.ThrowIfNull(CookThisSoon);
        ArgumentNullException.ThrowIfNull(History);
        ArgumentNullException.ThrowIfNull(NeverAgain);
        if (Recipes.GroupBy(recipe => recipe.Id).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Two cards share an id.", nameof(Recipes));
        }

        if (Rules.Adults is null || Rules.Adults.Count == 0)
        {
            throw new ArgumentException("There is no adult to plan for.", nameof(Rules));
        }

        if (BudgetSoFar is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BudgetSoFar), BudgetSoFar, "A budget so far cannot be negative.");
        }
    }
}
