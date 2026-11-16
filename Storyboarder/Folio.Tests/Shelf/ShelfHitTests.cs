using Folio.Domain.Shelf;

namespace Folio.Tests.Shelf;

/// <summary>
/// Which book a finger landed on.
///
/// <para>The shelf is one painted canvas per row, so a tap arrives as a coordinate and nothing else.
/// The walk that turns it back into a book is the same walk the painter does — bookend, spine, gap,
/// spine — and it lives here rather than in the view for the reason every other piece of this
/// screen's geometry does: a painter and a hit test that disagree put the wrong book on screen, and
/// neither of them is wrong on its own.</para>
/// </summary>
public class ShelfHitTests
{
    private static readonly ShelfBook First = new(Guid.NewGuid(), "One", null, 1, 40_000);
    private static readonly ShelfBook Second = new(Guid.NewGuid(), "Two", null, 2, 95_000);

    [Fact]
    public void AFingerOnTheFirstSpineFindsTheFirstBook()
    {
        var row = new ShelfRow(null, [First, Second]);

        Assert.Equal(First, row.At(2));
        Assert.Equal(First, row.At(First.WidthDp - 1));
    }

    [Fact]
    public void AFingerPastTheGapFindsTheSecondBook()
    {
        var row = new ShelfRow(null, [First, Second]);

        Assert.Equal(Second, row.At(First.WidthDp + ShelfRow.GapDp + 1));
    }

    /// <summary>
    /// A series row starts with a bookend, and a bookend is not a book. Missing this offsets every
    /// spine on every series shelf by fifteen points — enough to open the wrong book on a narrow one.
    /// </summary>
    [Fact]
    public void TheBookendIsNotABook()
    {
        var row = new ShelfRow("The Tidewater Books", [First, Second]);

        Assert.Null(row.At(3));
        Assert.Equal(First, row.At(ShelfComposer.BookendWidthDp + 2));
    }

    [Fact]
    public void TheGapBetweenTwoSpinesBelongsToNeither()
    {
        var row = new ShelfRow(null, [First, Second]);

        Assert.Null(row.At(First.WidthDp + 0.5));
    }

    [Fact]
    public void PastTheLastSpineIsShelf()
    {
        var row = new ShelfRow(null, [First, Second]);

        Assert.Null(row.At(row.WidthDp + 40));
        Assert.Null(row.At(-3));
    }

    [Fact]
    public void AnEmptyShelfHoldsNothing() =>
        Assert.Null(new ShelfRow(null, []).At(10));

    /// <summary>
    /// Where each spine stands, for the screen reader's buttons laid over the painting (audit W9.3, report 10 §8):
    /// the same walk as <see cref="ShelfRow.At"/>, so a button and a tap open the same book.
    /// </summary>
    [Fact]
    public void EachSpinesSpanIsWhereATapFindsIt()
    {
        var row = new ShelfRow("A series", [First, Second]);

        var spans = row.Spans;

        Assert.Equal([First, Second], spans.Select(span => span.Book).ToArray());
        Assert.All(spans, span => Assert.Equal(span.Book, row.At(span.LeftDp + 1)));
        Assert.Equal(ShelfComposer.BookendWidthDp, spans[0].LeftDp);
        Assert.Equal(First.WidthDp, spans[0].WidthDp);
    }
}
