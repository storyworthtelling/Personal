using Folio.Domain.Markup;
using Folio.Domain.Readiness;

namespace Folio.Tests.Markup;

/// <summary>
/// G6 — a mark lands on the lines it actually covers.
///
/// <para>The bug this reintroduces is an off-by-one on a span. It is the finding's R-6 and the
/// nastiest failure in the release, because <b>nothing can see it</b>: a strike-through one line
/// out is still a strike-through, the page still renders, the PDF still opens, and the only person
/// who can tell is the author, reading a note about a sentence it does not concern.</para>
///
/// <para>Both ends are half-open, matching <c>Scene</c>'s spans. A run ending exactly where a line
/// begins does not touch that line.</para>
/// </summary>
public class MarkPlacementTests
{
    // Four lines of sixty characters, the way the drawn page sets them.
    private static readonly IReadOnlyList<LineSpan> Lines =
    [
        new(0, 0, 60), new(1, 60, 120), new(2, 120, 180), new(3, 180, 240),
    ];

    private static Mark Run(int start, int end, MarkKind kind = MarkKind.Dele) =>
        new(kind, ReadinessTier.Should, start, end, "Cut · over-told", "The voice already showed it.", "Show vs tell");

    [Fact]
    public void ARunInsideOneLineTouchesOnlyThatLine()
    {
        Assert.Equal([1], MarkPlacement.Lines(Run(70, 90), Lines));
    }

    [Fact]
    public void ARunAcrossTwoLinesTouchesBoth()
    {
        Assert.Equal([1, 2], MarkPlacement.Lines(Run(70, 150), Lines));
    }

    /// <summary>
    /// The off-by-one itself: a run ending exactly at the next line's first character stops on the
    /// line before. Inclusive arithmetic would strike through a line the note never meant.
    /// </summary>
    [Fact]
    public void ARunEndingAtTheNextLinesStartDoesNotTouchIt()
    {
        Assert.Equal([1], MarkPlacement.Lines(Run(70, 120), Lines));
    }

    /// <summary>And the mirror: a run starting at a line's first character does touch it.</summary>
    [Fact]
    public void ARunStartingAtALinesFirstCharacterTouchesIt()
    {
        Assert.Equal([2], MarkPlacement.Lines(Run(120, 150), Lines));
    }

    [Fact]
    public void ARunSpanningEveryLineTouchesEveryLine()
    {
        Assert.Equal([0, 1, 2, 3], MarkPlacement.Lines(Run(0, 240), Lines));
    }

    /// <summary>A caret marks a point between characters, so it belongs to the line it sits in.</summary>
    [Fact]
    public void ACaretBelongsToTheLineItSitsIn()
    {
        Assert.Equal([2], MarkPlacement.Lines(Run(130, 130, MarkKind.Caret), Lines));
    }

    [Fact]
    public void AMarkOffThisPageTouchesNothing()
    {
        Assert.Empty(MarkPlacement.Lines(Run(500, 520), Lines));
        Assert.Empty(MarkPlacement.Lines(Run(900, 900, MarkKind.Caret), Lines));
    }

    [Fact]
    public void TheNoteAnchorsToTheFirstLineTheMarkTouches()
    {
        Assert.Equal(1, MarkPlacement.AnchorLine(Run(70, 150), Lines));
        Assert.Null(MarkPlacement.AnchorLine(Run(500, 520), Lines));
    }
}
