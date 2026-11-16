using Folio.Domain.Readiness;

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

/// <summary>One note in a chapter's list: its severity, the word naming it, and what it says.</summary>
public sealed record ChapterNote(ReadinessTier Severity, string Label, string Text, string Source)
{
    /// <summary>The finding's key (<see cref="FindingKey"/>), so the plan can link to the list it is in.</summary>
    public string FindingKey { get; init; } = string.Empty;
}

/// <summary>
/// The notes about one chapter that found no passage of their own (R7), printed as a list —
/// "Notes on this chapter" — at the chapter's head rather than as queries on its first line, which read
/// as notes about that line.
///
/// <para><see cref="Offset"/> is where the list goes: just past the chapter's title when it opens with
/// one (<see cref="FollowsTitle"/>), else at its first character, so the list is never printed after
/// the first line of a paragraph. Notes run most severe first, then in the order they were found.</para>
/// </summary>
public sealed record ChapterNoteList(int Chapter, int Offset, bool FollowsTitle, IReadOnlyList<ChapterNote> Notes);

/// <summary>What the report's findings come to once they are written on the manuscript.</summary>
public sealed record MarkedReport(IReadOnlyList<Mark> Marks, IReadOnlyList<UnplacedNote> Unplaced)
{
    public static MarkedReport Empty { get; } = new([], []);

    /// <summary>Each chapter's list of notes that could not be tied to a passage, in reading order.</summary>
    public IReadOnlyList<ChapterNoteList> ChapterNotes { get; init; } = [];

    /// <summary>Every finding offered to the manuscript, keyed, wherever it ended up (<see cref="FindingCatalogue"/>).</summary>
    public IReadOnlyList<FindingEntry> Findings { get; init; } = [];
}
