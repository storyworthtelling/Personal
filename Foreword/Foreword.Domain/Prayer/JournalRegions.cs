namespace Foreword.Domain.Prayer;

/// <summary>One writable band on a delivered page, in page points from the top-left.</summary>
public readonly record struct WritingRegion(string Name, float Left, float Top, float Width, float Height);

/// <summary>
/// Where the journal page's two writable bands are.
///
/// <para><b>Both ends come from here.</b> The page is painted from these numbers and the
/// transcription is told which region is which from the same ones (D17) — Foreword drew the page,
/// so it knows the coordinates and does not need a model to work them out.</para>
///
/// <para>The reason that matters: a line written under "Add to the list" becomes a request on
/// tomorrow's printed list, and the same line written two inches higher is a private journal entry.
/// If the painter and the transcription disagree about the boundary, the two swap — nothing errors,
/// and a private thought turns up on a printed page.</para>
/// </summary>
public static class JournalRegions
{
    public const string Journalling = "journalling";
    public const string Adding = "adding";

    /// <summary>The ruled writing lines, in points. A comfortable hand's height.</summary>
    public const float RuleSpacing = 25f;

    /// <summary>
    /// How many lines the add strip holds. A definite number, because the transcription is told how
    /// many to expect — "however many fitted" is not something a prompt can be told.
    /// </summary>
    public const int AddLines = 4;

    /// <summary>The left margin and the tab gutter, matching what the composers use.</summary>
    private const float Margin = 54f;

    private const float TabGutter = 38f;

    /// <summary>Clear of the header and its rule.</summary>
    private const float FirstLineTop = 106f;

    /// <summary>The strip's own heading, plus the gap that separates it from the free space above.</summary>
    private const float AddHeadingHeight = 22f;

    private const float FolioSpace = 26f;

    public static IReadOnlyList<WritingRegion> For(float pageWidth, float pageHeight)
    {
        var width = pageWidth - Margin - TabGutter;

        var addHeight = AddHeadingHeight + AddLines * RuleSpacing;
        var addTop = pageHeight - Margin - FolioSpace - addHeight;

        return
        [
            new WritingRegion(Journalling, Margin, FirstLineTop, width, addTop - FirstLineTop),

            // At the foot, under the free space rather than over it: writing runs downward, so a
            // strip at the top would be the first thing the pen meets and would collect whatever
            // was on the owner's mind rather than what they meant to add.
            new WritingRegion(Adding, Margin, addTop, width, addHeight),
        ];
    }
}
