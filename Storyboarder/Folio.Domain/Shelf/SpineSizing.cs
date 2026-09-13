namespace Folio.Domain.Shelf;

/// <summary>
/// How thick a book looks on the shelf, and how tall.
///
/// <para><b>Width is word count, and it is the shelf's only data.</b> Flyleaf sizes its spines by
/// how much an app is used; Folio has no such signal and does not want one — length is free to
/// measure, already known the moment a manuscript is ingested, and literally true of real books, so
/// a shelf of them tells the owner something true at a glance.</para>
///
/// <para><b>Height is deliberately not data.</b> It varies so a row reads as books rather than as a
/// bar chart, which means it must carry nothing: it is a hash of the title, stable across launches
/// so the shelf does not reshuffle itself when nothing has moved, and unordered with respect to
/// anything a reader could mistake for meaning.</para>
///
/// <para>Pure, so the arithmetic is settled away from a device the way the rest of the suite's
/// geometry is; the view is a thin painter.</para>
/// </summary>
public static class SpineSizing
{
    /// <summary>A novella still has to be wide enough to read a title down.</summary>
    public const int MinWidthDp = 22;

    /// <summary>Past this a long book stops getting wider, or it would crowd the shelf out.</summary>
    public const int MaxWidthDp = 42;

    public const int MinHeightDp = 128;
    public const int MaxHeightDp = 152;

    // Where the line's ends come from. The mockup gives the drawn RANGE outright — 22dp is labelled
    // "a short one" and 42dp "a long book" — but only one spine in it carries a stated length: The
    // Salt Line, 94,200 words, drawn at 34dp. These anchors are the round pair that puts that book
    // at 34.03dp, so the one hard number in the drawing is reproduced rather than approximated.
    // (20,000/150,000 was tried first and lands it at 33.4, which rounds to 33: a spine a pixel
    // thinner than the approved frame, for no reason anybody could later reconstruct.)
    private const int ShortWords = 10_000;
    private const int LongWords = 150_000;

    public static int WidthDp(int wordCount)
    {
        if (wordCount <= ShortWords)
        {
            return MinWidthDp;
        }

        if (wordCount >= LongWords)
        {
            return MaxWidthDp;
        }

        var across = (double)(wordCount - ShortWords) / (LongWords - ShortWords);
        return MinWidthDp + (int)Math.Round(across * (MaxWidthDp - MinWidthDp), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A stable height for this title, inside the band. FNV-1a rather than <c>string.GetHashCode</c>,
    /// which is randomised per process in .NET Core: the same book would be a different height on
    /// every launch, and the shelf would appear to shuffle itself.
    /// </summary>
    public static int HeightDp(string title)
    {
        var hash = 2_166_136_261u;
        foreach (var character in title ?? string.Empty)
        {
            hash = (hash ^ character) * 16_777_619u;
        }

        return MinHeightDp + (int)(hash % (uint)(MaxHeightDp - MinHeightDp + 1));
    }
}
