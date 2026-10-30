using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What the week card says about a dish at the time the week was planned: its title and the two markers the book draws beside a line.</summary>
public sealed record StoredDish(RecipeId Recipe, string Title, bool SlowCooker = false, bool IsNew = false);

/// <summary>One planned week as the run saved it: the week, when it was first planned, and the dishes it names.</summary>
public sealed record StoredMealWeek(MealWeek Week, DateTimeOffset PlannedAt, IReadOnlyList<StoredDish> Dishes)
{
    public DateOnly WeekStart => Week.WeekStart;
}

/// <summary>
/// The weeks the weekly meal run has planned, newest last, for Today's dinner card and the Meals tab to read. Only a planned week
/// is kept (a stop never replaces the last good plan), and only the latest <see cref="Kept"/> weeks: the one in progress, the one
/// being planned ahead, and a little behind. It holds words, days and times; no calendar entry, no name from a calendar.
/// </summary>
public sealed record MealWeekRecord(IReadOnlyList<StoredMealWeek> Weeks)
{
    public const int Kept = 4;

    public static MealWeekRecord Empty { get; } = new([]);

    public StoredMealWeek? For(DateOnly weekStart) => Weeks.FirstOrDefault(item => item.WeekStart == weekStart);

    /// <summary>The weeks with this one in place of any earlier copy of the same week.</summary>
    public MealWeekRecord With(StoredMealWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        if (!week.Week.IsPlanned)
        {
            throw new ArgumentException("Only a planned week is kept; a week that was not planned would replace the last good one.", nameof(week));
        }

        var others = Weeks.Where(item => item.WeekStart != week.WeekStart);
        return new MealWeekRecord(others.Append(week).OrderBy(item => item.WeekStart).TakeLast(Kept).ToList());
    }
}
