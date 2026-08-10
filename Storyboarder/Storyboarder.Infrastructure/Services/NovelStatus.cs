namespace Storyboarder.Infrastructure.Services;

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
    bool HasPreviousDraft = false);
