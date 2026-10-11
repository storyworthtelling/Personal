namespace Folio.Domain.Markup;

/// <summary>
/// Where a chapter sits in the stored manuscript. <see cref="Number"/> is the reading-order number a
/// finding's citation names ("Chapter 7"), which is <c>Chapter.Number</c> and not necessarily the
/// number printed in the book.
/// </summary>
public readonly record struct ChapterSpan(int Number, int StartOffset, int EndOffset);

/// <summary>
/// A note that could not be written on the page — no quotation that is really in the manuscript, and
/// no chapter it could honestly be pinned to — kept so the report can print it among the whole-book
/// notes instead of losing it. <see cref="Source"/> is the heading it used to be listed under.
/// </summary>
public sealed record UnplacedNote(string Source, string Chapter, string Text);

/// <summary>What the report's findings come to once they are written on the manuscript.</summary>
public sealed record MarkedReport(IReadOnlyList<Mark> Marks, IReadOnlyList<UnplacedNote> Unplaced)
{
    public static MarkedReport Empty { get; } = new([], []);
}
