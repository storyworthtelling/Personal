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
}
