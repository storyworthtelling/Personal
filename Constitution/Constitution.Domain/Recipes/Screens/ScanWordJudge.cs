namespace Constitution.Domain.Recipes.Screens;

/// <summary>Which list of a scanned card a word sits in.</summary>
public enum ScanLineKind
{
    Ingredient,
    Step,
}

/// <summary>
/// One word (or short run of words) a second reading doubts, with the line it is in and the two other readings it might be
/// (book p.14). <see cref="Reading"/> must be written exactly as it appears in the line; <see cref="Label"/> is the short name the
/// choice row carries ("Oven", "Sugar", "Fat"). At most two <see cref="Alternatives"/> are used.
/// </summary>
public sealed record UncertainWord(ScanLineKind Kind, int LineIndex, string Label, string Reading, IReadOnlyList<string> Alternatives);

/// <summary>
/// The seam for p.14's "uncertain words with two alternatives". Plate returns text only (D99), so the doubts can only come from a
/// later, second reading of that text; this is where it plugs in, and nothing about it is built yet. It sees the words and
/// nothing else. An implementation that cannot judge (no key, no network, not built) answers an empty list and does not throw, so
/// the card is simply shown once with Looks right.
/// </summary>
public interface IRecipeWordJudge
{
    IReadOnlyList<UncertainWord> Judge(RecipeReadPayload payload);
}

/// <summary>The judge used until a second reading exists: it doubts nothing, so no alternatives are offered.</summary>
public sealed class NoRecipeWordJudge : IRecipeWordJudge
{
    public IReadOnlyList<UncertainWord> Judge(RecipeReadPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return [];
    }
}
