using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// G17 — no two notes overlap, every leader lands on its anchor line, and a page whose notes
/// overflow ends early.
///
/// <para>The bug this reintroduces is placing notes <i>at</i> their anchors: the obvious
/// implementation, correct on the drawn page, and wrong the first time two long notes fall near each
/// other — which on a real manuscript is most pages. Overlapping notes do not fail anything; they
/// print on top of one another and the second is unreadable.</para>
///
/// <para>The second half is the owner's Release G gate decision: when the column runs past the foot,
/// the page breaks <b>early</b> so every note stays on the sheet with its line. A leader that
/// crossed a page boundary would point at nothing.</para>
/// </summary>
public class MarginPackingTests
{
    private const double Column = MarkupGeometry.ContentHeight;

    [Fact]
    public void ANoteSitsAtItsAnchorWhenNothingIsInTheWay()
    {
        var packed = MarginColumn.Place([(3, 100, 40)], Column);

        var note = Assert.Single(packed.Notes);
        Assert.Equal(100, note.Top, 3);
        Assert.False(note.NeedsLeader);
    }

    [Fact]
    public void TwoLongNotesNearEachOtherDoNotOverlap()
    {
        var packed = MarginColumn.Place([(3, 100, 60), (5, 130, 60)], Column);

        Assert.Equal(2, packed.Notes.Count);
        for (var i = 1; i < packed.Notes.Count; i++)
        {
            Assert.True(packed.Notes[i].Top >= packed.Notes[i - 1].Bottom,
                $"Note {i} starts at {packed.Notes[i].Top} but the one above ends at "
                + $"{packed.Notes[i - 1].Bottom} — they print on top of each other.");
        }
    }

    /// <summary>A pushed note keeps its anchor, which is what the leader is drawn to.</summary>
    [Fact]
    public void APushedNoteStillKnowsTheLineItBelongsTo()
    {
        var packed = MarginColumn.Place([(3, 100, 60), (5, 130, 60)], Column);

        var pushed = packed.Notes[1];
        Assert.Equal(5, pushed.AnchorLine);
        Assert.Equal(130, pushed.AnchorY, 3);
        Assert.True(pushed.NeedsLeader, "A note that moved must draw a leader back to its line.");
    }

    [Fact]
    public void ManyNotesStackInAnchorOrderAndNoneOverlaps()
    {
        var notes = Enumerable.Range(0, 12)
            .Select(i => (Line: i, AnchorY: i * 20.0, Height: 44.0)).ToList();

        var packed = MarginColumn.Place(notes, Column);

        Assert.Equal(
            packed.Notes.Select(note => note.AnchorLine).OrderBy(line => line),
            packed.Notes.Select(note => note.AnchorLine));
        for (var i = 1; i < packed.Notes.Count; i++)
        {
            Assert.True(packed.Notes[i].Top >= packed.Notes[i - 1].Bottom);
        }
    }

    /// <summary>
    /// The gate decision: the page ends early rather than carrying a note over. The text stops at
    /// the anchor of the first note that would not fit, so that note and its line arrive together on
    /// the next page.
    /// </summary>
    [Fact]
    public void APageWhoseNotesOverflowEndsEarly()
    {
        var notes = Enumerable.Range(0, 40)
            .Select(i => (Line: i, AnchorY: i * 15.0, Height: 50.0)).ToList();

        var packed = MarginColumn.Place(notes, Column);

        Assert.True(packed.NotesDeferred > 0, "Forty notes of 50pt cannot fit the column.");
        Assert.True(packed.TextHeight < Column,
            "The page ran its full height while deferring notes — a note is now on a different page "
            + "from the line it concerns.");
        Assert.All(packed.Notes, note => Assert.True(note.Bottom <= Column));
    }

    /// <summary>Every note that was kept sits wholly on the page — no leader points off the sheet.</summary>
    [Fact]
    public void NoKeptNoteRunsPastTheFoot()
    {
        var notes = Enumerable.Range(0, 20)
            .Select(i => (Line: i, AnchorY: i * 28.0, Height: 46.0)).ToList();

        var packed = MarginColumn.Place(notes, Column);

        Assert.All(packed.Notes, note =>
        {
            Assert.True(note.Bottom <= Column + 0.001);
            Assert.True(note.AnchorY <= packed.TextHeight + 0.001,
                "A leader reaches to a line the page no longer prints.");
        });
    }

    [Fact]
    public void APageWithNoNotesKeepsItsWholeHeight()
    {
        var packed = MarginColumn.Place([], Column);

        Assert.Empty(packed.Notes);
        Assert.Equal(Column, packed.TextHeight, 3);
        Assert.Equal(0, packed.NotesDeferred);
    }

    /// <summary>
    /// One note taller than the whole column is kept rather than deferred for ever. Deferring it
    /// would set the text height to its anchor, print nothing, and produce the same page again.
    /// </summary>
    [Fact]
    public void ANoteTallerThanThePageDoesNotStallTheDocument()
    {
        var packed = MarginColumn.Place([(2, 40, Column + 200)], Column);

        Assert.Single(packed.Notes);
        Assert.Equal(Column, packed.TextHeight, 3);
    }
}
