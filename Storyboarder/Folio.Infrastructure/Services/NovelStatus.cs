namespace Folio.Infrastructure.Services;

public sealed record PhaseProgress(int Done, int Total);

// A read model summarizing a manuscript's review progress for the status page. Every long-running stage
// carries a Done/Total so the card can say what the worker is on and how much of it is left:
// Chunks + ReferenceSheets cover the design-bible stage, SceneImages the per-scene evidence images, and
// AnalysisNotes the map step of the report.
public sealed record NovelStatus(
    Guid Id,
    string Title,
    string Stage,
    string? LastError,
    PhaseProgress Chunks,
    PhaseProgress ReferenceSheets,
    PhaseProgress SceneImages,
    PhaseProgress AnalysisNotes,
    PhaseProgress Analysis,
    bool AnalysisReady,
    // True only when the pipeline has genuinely stalled and needs the user. A transient error the worker
    // is still auto-retrying leaves this false, so the card doesn't cry wolf while it self-heals.
    bool NeedsAttention = false,
    // What this review is projected to cost, surfaced only while it is still cheap to abandon (before any
    // scene image has been rendered). Null once rendering starts or before the manuscript is ingested.
    Domain.Pipeline.RunEstimate? Estimate = null,
    // When a provider rate limit last deferred this manuscript, the (future) UTC instant it expects to try
    // again — set only when still in the future, so the card can say "resumes ~3:47 pm" in local time.
    DateTimeOffset? RateLimitedUntilUtc = null,
    // Which draft of its project this is (1 for a first/only draft), and whether it has a predecessor to
    // diff against. Drives the card's draft badge and its "What changed" action.
    int DraftNumber = 1,
    bool HasPreviousDraft = false,
    // Whether this book belongs to a series and, if so, its book number (1 = first). Drives the card's
    // "Book N" badge (Phase 5). InSeries is false for a standalone book, where the sequence is meaningless.
    bool InSeries = false,
    int SeriesSequence = 1,
    // Which series, when there is one. The shelf groups by it: without an identity every book is
    // its own series of one, and a trilogy takes three shelves with three sets of bookends.
    Guid? SeriesId = null,
    // What the series is called: the owner's name for it, or the first book's title until they give
    // it one. Resolved here so no screen has to work it out and none of them can disagree.
    string? SeriesName = null,
    // Whether the owner has put this book on the shelf. The pipeline never sets it: finishing a
    // review and putting the book away are separate events, and only the second is the owner's.
    bool Shelved = false,
    // How long the book is, which is what the shelf draws its spine width from.
    int WordCount = 0);
