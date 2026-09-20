namespace Folio.Domain.Shelf;

/// <summary>One book as the shelf needs to know it.</summary>
/// <param name="Series">The series it belongs to, or null for a standalone book.</param>
/// <param name="Sequence">Its number within that series; ignored when standalone.</param>
/// <param name="SeriesName">
/// What that series is called. Passed in rather than worked out here: the owner can name a series,
/// and a name derived from the first book's title would overrule them the moment they did.
/// </param>
public sealed record ShelfBook(
    Guid Id, string Title, string? Series, int Sequence, int WordCount, string? SeriesName = null)
{
    /// <summary>How wide this book stands, from its length. The shelf's only data.</summary>
    public int WidthDp => SpineSizing.WidthDp(WordCount);

    /// <summary>How tall it stands. Varied so the row reads as books; carries nothing.</summary>
    public int HeightDp => SpineSizing.HeightDp(Title);
}

/// <summary>
/// A single shelf: the books standing on it, and what the series is called if it is one.
///
/// <para><paramref name="SeriesName"/> is what the series is <b>called</b>, not the key it is
/// grouped by: the owner names a series, and until they do it is called after its first book. The
/// caller resolves that, so this never overrules a name somebody chose.</para>
/// </summary>
public sealed record ShelfRow(string? SeriesName, IReadOnlyList<ShelfBook> Books)
{
    /// <summary>A series row is bracketed by bookends; a standalone row is not.</summary>
    public bool HasBookends => SeriesName is not null;

    public int WidthDp => Books.Sum(book => book.WidthDp) + (Books.Count > 0 ? (Books.Count - 1) * GapDp : 0);

    /// <summary>The 2dp hair between spines in the approved frame.</summary>
    public const int GapDp = 2;

    /// <summary>
    /// The book standing at <paramref name="x"/> points from this row's left edge, or null for a
    /// bookend, a gap, or bare shelf.
    ///
    /// <para>The shelf is painted as one canvas, so a tap arrives as a coordinate. This is the
    /// painter's own walk read backwards, and it is here rather than in the view because a painter
    /// and a hit test that drift apart open the wrong book while both look correct.</para>
    /// </summary>
    public ShelfBook? At(double x)
    {
        var cursor = HasBookends ? (double)ShelfComposer.BookendWidthDp : 0;

        foreach (var book in Books)
        {
            if (x >= cursor && x < cursor + book.WidthDp)
                return book;
            cursor += book.WidthDp + GapDp;
        }

        return null;
    }
}

/// <summary>
/// Deals the library onto shelves.
///
/// <para><b>A series always gets a shelf to itself</b> — the rule the approved frame states, and the
/// reason is in the drawing: its bookends bracket the whole row. Mixed in with standalone books they
/// would bracket a run inside a row instead, which reads as an arbitrary grouping rather than a set.
/// A long series takes a second shelf rather than squeezing its spines, because width is the shelf's
/// one piece of data and squeezing it would make the data a lie.</para>
///
/// <para>Pure, and tested away from a device: the view is a painter.</para>
/// </summary>
public static class ShelfComposer
{
    /// <summary>
    /// The drawable width of a shelf in dp. A Pixel 8a well is 393dp less the 66dp binder gutter and
    /// the well's own padding; the frame draws its ledges 287dp wide.
    /// </summary>
    public const int ShelfWidthDp = 287;

    /// <summary>Bookends stand at both ends of a series shelf and take room from it.</summary>
    public const int BookendWidthDp = 15;

    public static IReadOnlyList<ShelfRow> Compose(IReadOnlyList<ShelfBook> books)
    {
        var rows = new List<ShelfRow>();

        // Series first, each on its own shelves, in the order their first book arrived.
        foreach (var series in books.Where(book => book.Series is not null)
                     .GroupBy(book => book.Series!, StringComparer.Ordinal))
        {
            var ordered = series
                .OrderBy(book => book.Sequence)
                .ThenBy(book => book.Title, StringComparer.Ordinal)
                .ToList();

            rows.AddRange(Wrap(ordered, ordered[0].SeriesName ?? ordered[0].Title, ShelfWidthDp - (2 * BookendWidthDp)));
        }

        rows.AddRange(Wrap(books.Where(book => book.Series is null).ToList(), seriesName: null, ShelfWidthDp));

        return rows;
    }

    // Fills a shelf until the next spine would not fit, then starts another. A single book too wide
    // for an empty shelf still gets one — it is on the shelf or it is nowhere.
    private static IEnumerable<ShelfRow> Wrap(IReadOnlyList<ShelfBook> books, string? seriesName, int available)
    {
        var row = new List<ShelfBook>();
        var used = 0;

        foreach (var book in books)
        {
            var needed = book.WidthDp + (row.Count > 0 ? ShelfRow.GapDp : 0);
            if (row.Count > 0 && used + needed > available)
            {
                yield return new ShelfRow(seriesName, row);
                row = [];
                used = 0;
                needed = book.WidthDp;
            }

            row.Add(book);
            used += needed;
        }

        if (row.Count > 0)
        {
            yield return new ShelfRow(seriesName, row);
        }
    }
}
