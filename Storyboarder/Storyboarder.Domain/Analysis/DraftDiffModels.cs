namespace Storyboarder.Domain.Analysis;

// Whether a finding appeared or disappeared between two drafts.
public enum FindingChange
{
    Resolved,
    New
}

// One revision-report finding that changed between a draft and the one before it, tagged with the
// report section it came from (plot holes, inconsistencies, inaccuracies, pacing) and the chapter it
// cites. "Resolved" means the previous draft raised it and the new one no longer does; "New" is the
// reverse. Findings are matched on their detail text alone — the honest, explainable definition — so a
// reworded-but-same issue reads as one resolved plus one new rather than being silently paired.
public sealed record DiffFinding(FindingChange Change, string Category, string Chapter, string Detail);

// One panel the new draft has to re-illustrate: the stretch of manuscript it sits on changed, so the
// previous draft's render can't be carried forward. Carries the panel's reading position and its new
// image, plus the previous draft's panel that occupied the same position — the "before" for a
// side-by-side — when one exists (null once panels have been added or removed above it).
public sealed record PanelChange(
    int VolumeNumber,
    int PageNumber,
    int PanelNumber,
    string? NewImagePath,
    string? PreviousImagePath);

// What changed between a draft and the draft it was made from: the report findings resolved and newly
// raised, and the panels whose source text moved. Every other panel the pipeline reuses, so
// ReusablePanelCount is how much of the previous illustration carries forward untouched. A first draft
// (no predecessor) diffs to None.
public sealed record DraftDiff(
    Guid DraftId,
    Guid? PreviousDraftId,
    IReadOnlyList<DiffFinding> ResolvedFindings,
    IReadOnlyList<DiffFinding> NewFindings,
    IReadOnlyList<PanelChange> ChangedPanels,
    int ReusablePanelCount)
{
    public static DraftDiff None(Guid draftId) =>
        new(draftId, null, [], [], [], 0);
}
