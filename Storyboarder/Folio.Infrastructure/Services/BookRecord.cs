using Folio.Domain.Analysis;
using Folio.Domain.Readiness;

namespace Folio.Infrastructure.Services;

/// <summary>One draft of a book, as the book page lists it.</summary>
/// <param name="ReportReady">Whether this draft's own report is rendered and still on the device.</param>
public sealed record BookDraft(Guid Id, int Number, DateTimeOffset Created, int WordCount, bool ReportReady = false);

/// <summary>
/// A whole book: its drafts, how ready each of them was, and what there is to open.
///
/// <para>Keyed by <b>project</b>, not by manuscript. Every other read in this service answers about
/// one draft, because every other screen is about work in progress; the book page's subject is the
/// book, and its drafts are its content. Opened from draft one or from draft three it is the same
/// page — which is what lets the shelf and the desk both hand it whichever draft they happen to be
/// drawing.</para>
///
/// <para>It carries data and no phrasing. "Three drafts" and "94,200 words" are the page's words
/// and belong where the page's other words are.</para>
/// </summary>
public sealed record BookRecord(
    Guid ProjectId,
    /// <summary>The newest draft — whose report the open action opens, and whose length is the book's.</summary>
    Guid LatestDraftId,
    string Title,
    string? CoverPath,
    string Stage,
    string? SeriesName,
    int SeriesSequence,
    bool InSeries,
    /// <summary>
    /// How many BOOKS the series holds — distinct projects, not drafts. A series of one is a series
    /// whose later books have not been started, which the page says by not counting them.
    /// </summary>
    int SeriesLength,
    IReadOnlyList<BookDraft> Drafts,
    /// <summary>
    /// One reading per draft that HAS a report, never one per draft. A draft still being read has
    /// no counts, and a zero drawn for it would say its Musts were fixed rather than not yet found.
    /// </summary>
    IReadOnlyList<DraftFindings> Readiness,
    /// <summary>Whether the latest draft's report has been rendered and is still on the device.</summary>
    bool ReportReady,
    string Genre,
    /// <summary>
    /// The latest draft's length against its genre norm, from the expansion read. Empty until that
    /// read has run, which is the ordinary state of a book still being reviewed.
    /// </summary>
    LengthAssessment Length);
