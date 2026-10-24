namespace Constitution.Domain.Recipes;

/// <summary>The words the recipe box shows (book pp.13–14). Static copy, written once; none of it is composed.</summary>
public static class RecipeBoxText
{
    public const string Invitation = "Ten family favourites, and the planning can begin.";

    public const string Footnote = "Weekly meal sheets start on the Thursday after the tenth card.";

    public const string ScanAction = "Scan a recipe card";

    public const string AddAction = "Add a recipe";

    /// <summary>The search field's label: "Find a recipe · 23 cards".</summary>
    public static string FindLabel(int cards) => "Find a recipe · " + cards + (cards == 1 ? " card" : " cards");
}

/// <summary>One of the ten card places: a card is in it, or it is a dashed outline waiting.</summary>
public enum PlaceState
{
    Filled,
    Dashed,
}

/// <summary>
/// What the Meals tab shows while the box is short of ten (C-6.2.5, p.14, D62): the invitation, the
/// counter ("3 of 10"), ten places, and the primary action. <b>Planning waits</b> and so do a Reference
/// notice and the slate bookmark (D91); at ten the invitation goes and the ordinary box takes over.
/// </summary>
public sealed record EmptyBoxState(
    int Cards,
    bool PlanningMayStart,
    string Counter,
    IReadOnlyList<PlaceState> Places,
    string Invitation,
    string Footnote,
    string PrimaryAction,
    bool WaitsWithReferenceNotice,
    bool BookmarkWaits)
{
    /// <summary>Whether the invitation, counter and places are on the page. Fewer than ten cards.</summary>
    public bool ShowsInvitation => !PlanningMayStart;
}

/// <summary>
/// Counting to ten. The invitation is <b>static Serif-italic copy, not a voice seat</b> (the book: "it isn't
/// one of the six voice seats"): nothing here composes, calls a model or reads the owner's profile, so the empty
/// box reads the same on a phone with no key. Which cards count is every card in the box; a pile is not ranked.
/// </summary>
public static class EmptyBox
{
    /// <summary>How many family favourites the household offers before the first plan (the brief's first run).</summary>
    public const int Target = 10;

    public static EmptyBoxState For(RecipeBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return For(box.Count);
    }

    public static EmptyBoxState For(int cards)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cards);
        var filled = Math.Min(cards, Target);
        var short10 = cards < Target;
        var places = Enumerable.Range(0, Target).Select(place => place < filled ? PlaceState.Filled : PlaceState.Dashed).ToList();
        return new EmptyBoxState(
            cards,
            PlanningMayStart: !short10,
            Counter: filled + " of " + Target,
            places,
            RecipeBoxText.Invitation,
            RecipeBoxText.Footnote,
            short10 ? RecipeBoxText.ScanAction : RecipeBoxText.AddAction,
            WaitsWithReferenceNotice: short10,
            BookmarkWaits: short10);
    }
}
