namespace Constitution.Domain.Recipes.Screens;

/// <summary>What a picked card offers about next week.</summary>
public enum CookSoonState
{
    /// <summary>The button: Cook this soon.</summary>
    Offer,

    /// <summary>The card says "Planned for next week". Success is these words, never a toast (DESIGN.md section 7).</summary>
    Planned,

    /// <summary>The planner was not there; the card says so and the button stays.</summary>
    Unavailable,
}

/// <summary>
/// Cook this soon (C-6.2.3): place the dish in next week's plan and let the card say so in its own words. The flag lives in
/// the plan the planner keeps, not on the recipe record, so the card carries nothing new.
/// </summary>
public static class CookSoon
{
    public const string OfferWords = "Cook this soon";
    public const string PlannedWords = "Planned for next week";
    public const string NotKeptWords = "Next week's plan could not be changed, so nothing was placed.";
    public const string UnavailableWords = "Planning is not set up yet, so nothing was placed.";

    public static string Words(CookSoonState state) => state switch
    {
        CookSoonState.Offer => OfferWords,
        CookSoonState.Planned => PlannedWords,
        CookSoonState.Unavailable => UnavailableWords,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No words for this state."),
    };

    /// <summary>Whether the card still shows the button. Only a planned card has put it away.</summary>
    public static bool OffersAButton(CookSoonState state) => state != CookSoonState.Planned;

    public static CookSoonState StateOf(RecipeId card, IReadOnlySet<RecipeId> plannedNextWeek)
    {
        ArgumentNullException.ThrowIfNull(plannedNextWeek);
        return plannedNextWeek.Contains(card) ? CookSoonState.Planned : CookSoonState.Offer;
    }

    /// <summary>Presses the button. A card already planned places nothing a second time.</summary>
    public static CookSoonState Press(ICookSoonPlanner planner, RecipeId card, CookSoonState current)
    {
        ArgumentNullException.ThrowIfNull(planner);
        if (current == CookSoonState.Planned)
        {
            return CookSoonState.Planned;
        }

        return planner.PlaceNextWeek(card) switch
        {
            CookSoonResult.Placed or CookSoonResult.AlreadyPlaced => CookSoonState.Planned,
            CookSoonResult.NoPlannerYet => CookSoonState.Unavailable,
            var other => throw new InvalidOperationException("The planner answered with something unknown: " + other + "."),
        };
    }
}
