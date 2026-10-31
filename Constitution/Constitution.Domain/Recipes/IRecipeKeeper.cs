namespace Constitution.Domain.Recipes;

/// <summary>What keeping a card came to: it is in the household's box, or the sentence for why it is not.</summary>
public sealed record RecipeKeepResult(string? Refusal)
{
    /// <summary>The card was written.</summary>
    public static RecipeKeepResult Done { get; } = new((string?)null);

    public bool IsKept => Refusal is null;

    public static RecipeKeepResult Refused(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        return new RecipeKeepResult(sentence);
    }
}

/// <summary>
/// Where a card the owner has checked goes: into the household file as a <c>recipe</c> record (<c>RecipeMaps</c>, PBI C-6.5.3).
/// This is the only door a scanned card leaves through, and what crosses it is a <see cref="Recipe"/>, which holds words and
/// nothing else. <see cref="Keep"/> may be slow, so the head calls it off the UI thread; it throws <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/> when the write failed, and answers a refusal in a sentence when there is nowhere
/// to write (no household file on this phone yet).
/// </summary>
public interface IRecipeKeeper
{
    RecipeKeepResult Keep(Recipe recipe);
}

/// <summary>The keeper used until the household file is wired: it keeps nothing, and says so.</summary>
public sealed class NoRecipeKeeper : IRecipeKeeper
{
    public const string Sentence = "There is no household file on this phone yet, so the card could not be kept.";

    public RecipeKeepResult Keep(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return RecipeKeepResult.Refused(Sentence);
    }
}
