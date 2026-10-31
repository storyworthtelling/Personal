using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Screens;

/// <summary>What the week card says about a dish: its title, and the two markers the book draws beside a line.</summary>
public sealed record DishFacts(string Title, bool SlowCooker = false, bool IsNew = false);

/// <summary>Where an evening out is, when dinner is, when to leave and who goes; the week itself holds only "out" and a reason.</summary>
public sealed record EatOutDetail(string Place, TimeOnly DinnerAt, TimeOnly LeaveAt, string Who);

/// <summary>
/// Everything the This week view and Today's dinner card are made of at one reading: the week, the dishes by card, an evening
/// out's details when the household noted them, and the Sunday the week was approved on, when known.
/// </summary>
public sealed record MealWeekSnapshot(MealWeek Week, IReadOnlyDictionary<RecipeId, DishFacts> Dishes)
{
    public IReadOnlyDictionary<DateOnly, EatOutDetail>? EatingOut { get; init; }

    public DateOnly? ApprovedOn { get; init; }
}

/// <summary>What reading the week gave: a week, the plain fact that there is none yet, or that it cannot be read now.</summary>
public abstract record MealWeekScreenRead
{
    public sealed record NoPlanYet : MealWeekScreenRead;

    public sealed record Ready(MealWeekSnapshot Snapshot) : MealWeekScreenRead;

    /// <summary>There may be a week, but it cannot be read now; the reason is a fixed sentence that quotes nothing.</summary>
    public sealed record Unavailable(string Reason) : MealWeekScreenRead;
}

/// <summary>
/// Where the approved week comes from. The weekly meal run fills this later; until it does the view says there is no plan.
/// <see cref="Read"/> may be slow, so the head calls it off the UI thread.
/// </summary>
public interface IMealWeekSource
{
    MealWeekScreenRead Read(DateTimeOffset now);
}

/// <summary>The source used until the run's saved week is wired: it has none, and says so.</summary>
public sealed class NoMealWeekSource : IMealWeekSource
{
    public MealWeekScreenRead Read(DateTimeOffset now) => new MealWeekScreenRead.NoPlanYet();
}
