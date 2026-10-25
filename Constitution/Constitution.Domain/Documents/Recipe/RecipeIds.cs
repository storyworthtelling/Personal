namespace Constitution.Domain.Documents;

/// <summary>
/// The ids of the targets on the recipe pages (book p.8). The ingredients page draws exactly one, the
/// notes margin every recipe page carries since D35; nothing on it is ticked or circled, so the page
/// has no shopping target. The literal is frozen: it is the word the ink reader's notes rule will name,
/// and it is the same word as the workout pages' margin because it is the same margin
/// (<c>RecipeIdsTests.TheNotesMarginIsTheSameMarginAsTheWorkoutPages</c>).
/// </summary>
public static class RecipeIds
{
    /// <summary>The 91 point notes column; what the cook writes there goes to the taste profile (D35).</summary>
    public const string Notes = "notes";

    /// <summary>
    /// The "We didn't cook this tonight" tick at the foot of the rating page (D68). Any mark is a tick; ticked, the ratings
    /// on the page are ignored. Frozen: the ink reader's rule names it.
    /// </summary>
    public const string DidntCook = "didnt-cook";

    /// <summary>
    /// The circle group of the n'th eater's row, counting from 1 in the order the page prints them: <c>rate-1</c>. Its lanes
    /// are <c>rate-1-1</c>, <c>rate-1-2</c>, <c>rate-1-3</c>. This is the id the ink resolver reports the row's answer under,
    /// so it is the <c>Group</c> of the row's <c>TasteRatingRow</c>. Positional rather than the name, so two eaters who share a
    /// first name cannot share a group.
    /// </summary>
    public static string RatingGroup(int row)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        return "rate-" + row.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The words printed in the three circle lanes, in lane order. <c>TasteWords.FromPrinted</c> reads each back to its verdict.</summary>
    public static IReadOnlyList<string> RatingWords { get; } = ["Loved", "Fine", "Never again"];
}
