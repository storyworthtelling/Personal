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

// What changed between a draft and the draft it was made from: the report findings resolved and newly
// raised. A first draft (no predecessor) diffs to None. (Before/after scene-image pairs and full
// cross-draft finding history are the Phase 2 draft-loop investment; this carries the finding diff.)
public sealed record DraftDiff(
    Guid DraftId,
    Guid? PreviousDraftId,
    IReadOnlyList<DiffFinding> ResolvedFindings,
    IReadOnlyList<DiffFinding> NewFindings)
{
    public static DraftDiff None(Guid draftId) =>
        new(draftId, null, [], []);
}
