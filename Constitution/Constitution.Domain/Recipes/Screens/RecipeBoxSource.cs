namespace Constitution.Domain.Recipes.Screens;

/// <summary>Everything the box is made of at one reading.</summary>
public sealed record RecipeBoxSnapshot(
    IReadOnlyList<Recipe> Cards,
    IReadOnlyDictionary<RecipeId, CardHistory> Histories,
    IReadOnlySet<RecipeId> PlannedNextWeek)
{
    /// <summary>What the household's cooking says about the card, or <see cref="CardHistory.Never"/>.</summary>
    public CardHistory HistoryOf(RecipeId card) => Histories.TryGetValue(card, out var history) ? history : CardHistory.Never;
}

/// <summary>What reading the box gave: the cards, or the plain fact that there is no household file to read them from.</summary>
public abstract record RecipeBoxRead
{
    public sealed record NoHouseholdFile(string Sentence) : RecipeBoxRead;

    public sealed record Ready(RecipeBoxSnapshot Snapshot) : RecipeBoxRead;

    /// <summary>There may be cards, but they cannot be read now; the reason is a fixed sentence that quotes nothing.</summary>
    public sealed record Unavailable(string Reason) : RecipeBoxRead;
}

/// <summary>Whether next week's plan took a dish.</summary>
public enum CookSoonResult
{
    Placed,
    AlreadyPlaced,

    /// <summary>The meal planner (F-6.3) is not there to take it.</summary>
    NoPlannerYet,
}

/// <summary>The planner's door for Cook this soon (C-6.2.3): put this dish in next week's plan. Harmless to repeat.</summary>
public interface ICookSoonPlanner
{
    CookSoonResult PlaceNextWeek(RecipeId card);
}

/// <summary>
/// Where the box's cards come from and where its changes go. The household file fills this later; until then the tab
/// says there is none. <see cref="Read"/> may be slow, so the head calls it off the UI thread.
/// </summary>
public interface IRecipeBoxSource : ICookSoonPlanner
{
    RecipeBoxRead Read();

    /// <summary>
    /// Keeps a move of a card to another cuisine, and keeps its inverse the same way (an undo is a move). Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it could not be written, and
    /// <see cref="InvalidOperationException"/> when there is nothing to change or the card is gone.
    /// </summary>
    void Move(CuisineMove move);
}

/// <summary>The source used until the household file is wired: it has no cards, and says so.</summary>
public sealed class NoRecipeBoxSource : IRecipeBoxSource
{
    public const string Sentence = "There is no household file on this phone yet, so the recipe box has no cards to show.";

    public RecipeBoxRead Read() => new RecipeBoxRead.NoHouseholdFile(Sentence);

    public void Move(CuisineMove move) => throw new InvalidOperationException("There is no recipe box to change yet.");

    public CookSoonResult PlaceNextWeek(RecipeId card) => CookSoonResult.NoPlannerYet;
}
