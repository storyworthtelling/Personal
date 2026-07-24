namespace Storyboarder.Infrastructure.Services;

public sealed record PhaseProgress(int Done, int Total);

// A panel parked awaiting the user's decision after a safety block, for the review UI. Categories are
// the flagged HARM_CATEGORY_* names that would be relaxed on an approved re-render.
public sealed record BlockedPanel(
    Guid PanelId, int PanelNumber, string VisualPrompt, string? Dialogue, string? Caption,
    IReadOnlyList<string> Categories, string? Reason);

public sealed record VolumeStatus(int Number, string Title, bool PdfReady, bool Failed = false);

// The prohibited/IP-blocked panel a Cancelled novel is stuck on, surfaced so the user can rework its
// art direction and resume. Reason is the provider's block message; VisualPrompt is what gets reworked.
public sealed record ProhibitedPanel(Guid PanelId, int PanelNumber, string VisualPrompt, string? Reason);

// A read model summarizing a novel's progress for the status page / API. Every long-running stage
// carries a Done/Total so the card can say what the worker is on and how much of it is left:
// Chunks + ReferenceSheets cover the design-bible stage, Storyboard the storyboarding stage,
// Panels/Pages/Volumes the render-and-bind stages, and AnalysisNotes the map step of the report.
public sealed record NovelStatus(
    Guid Id,
    string Title,
    string Stage,
    string? LastError,
    PhaseProgress Chunks,
    PhaseProgress ReferenceSheets,
    PhaseProgress Storyboard,
    PhaseProgress Panels,
    PhaseProgress Pages,
    PhaseProgress Volumes,
    IReadOnlyList<VolumeStatus> VolumeList,
    PhaseProgress AnalysisNotes,
    PhaseProgress Analysis,
    bool AnalysisReady,
    string? CoverThumbPath = null,
    // True only when the pipeline has genuinely stalled and needs the user (a storyboard window that
    // exhausted its retry budget, or one or more panels safety-blocked and waiting on the user's OK).
    // A transient error the worker is still auto-retrying leaves this false, so the card doesn't cry
    // wolf while it self-heals.
    bool NeedsAttention = false,
    // Panels safety-blocked mid-render and parked until the user approves relaxing the flagged category
    // (or skips them). Non-zero means the book can't finalize until the user reviews them.
    int PendingApprovals = 0,
    // When a provider rate limit last deferred this novel, the (future) UTC instant it expects to try
    // again — set only when still in the future, so the card can say "resumes ~3:47 pm" in local time.
    DateTimeOffset? RateLimitedUntilUtc = null);
