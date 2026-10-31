using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>What one evening's dinner is. A closed list: the page draws each differently and a stored week keeps a literal word for each.</summary>
public enum DinnerKind
{
    /// <summary>A card from the box, cooked by one adult.</summary>
    Cooked,

    /// <summary>What the evening before left over. The rules plan it only on the evening after a cooked one.</summary>
    Leftovers,

    /// <summary>Dinner out. The rules planner never plans it (money is not read yet); an authored week may.</summary>
    EatOut,

    /// <summary>Nothing is planned for the evening, and <see cref="DinnerChoice.Reason"/> says why in plain words.</summary>
    NothingPlanned,
}

/// <summary>
/// One evening's dinner. It holds no word about who or what wrote it (a rules week and an authored week are the same
/// kind of thing, and the page cannot tell); the origin lives only on <see cref="MealOutcome"/>.
/// </summary>
public sealed record DinnerChoice
{
    private DinnerChoice(
        DateOnly day,
        DinnerKind kind,
        RecipeId? recipe,
        string? cook,
        DateTimeOffset? cookStart,
        DateTimeOffset? cookEnd,
        bool isStretch,
        bool childrenEatDifferently,
        string reason)
    {
        Day = day;
        Kind = kind;
        Recipe = recipe;
        Cook = cook;
        CookStart = cookStart;
        CookEnd = cookEnd;
        IsStretch = isStretch;
        ChildrenEatDifferently = childrenEatDifferently;
        Reason = reason;
    }

    public DateOnly Day { get; }

    public DinnerKind Kind { get; }

    /// <summary>The card, for a <see cref="DinnerKind.Cooked"/> dinner; otherwise null.</summary>
    public RecipeId? Recipe { get; }

    /// <summary>The adult who cooks, by the name the planning rules know them by; null unless cooked.</summary>
    public string? Cook { get; }

    /// <summary>When the cook block starts, inside the cook's free gap; null unless cooked.</summary>
    public DateTimeOffset? CookStart { get; }

    /// <summary>When the cook block ends: the family dinner's start. Null unless cooked.</summary>
    public DateTimeOffset? CookEnd { get; }

    /// <summary>True for the week's one stretch dish: a card not cooked lately.</summary>
    public bool IsStretch { get; }

    /// <summary>True when the children are at the table and are given a different meal from the adults' dish.</summary>
    public bool ChildrenEatDifferently { get; }

    /// <summary>The evening in a plain sentence or two. Never says who or what wrote the week.</summary>
    public string Reason { get; }

    public static DinnerChoice Cooked(
        DateOnly day,
        RecipeId recipe,
        string cook,
        DateTimeOffset cookStart,
        DateTimeOffset cookEnd,
        bool isStretch,
        bool childrenEatDifferently,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cook);
        if (cookEnd <= cookStart)
        {
            throw new ArgumentException("A cook block has to end after it starts.", nameof(cookEnd));
        }

        return new DinnerChoice(day, DinnerKind.Cooked, recipe, cook.Trim(), cookStart, cookEnd, isStretch, childrenEatDifferently, Said(reason));
    }

    public static DinnerChoice Leftovers(DateOnly day, string reason) =>
        new(day, DinnerKind.Leftovers, null, null, null, null, false, false, Said(reason));

    public static DinnerChoice EatOut(DateOnly day, string reason) =>
        new(day, DinnerKind.EatOut, null, null, null, null, false, false, Said(reason));

    public static DinnerChoice NothingPlanned(DateOnly day, string reason) =>
        new(day, DinnerKind.NothingPlanned, null, null, null, null, false, false, Said(reason));

    private static string Said(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return reason.Trim();
    }
}
