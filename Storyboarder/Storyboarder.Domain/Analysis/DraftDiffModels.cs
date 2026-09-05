using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Analysis;

// How a finding thread changed between a draft and the one before it.
public enum FindingChange
{
    Resolved,   // open in the previous draft, gone now
    New,        // first appears this draft
    Regressed   // was present, then fixed, and has reappeared
}

// One revision-report finding that changed between a draft and the one before it, tagged with the story-
// integrity category it belongs to and the chapter it cites. Matched across drafts by the finding's stable
// ThreadId (assigned by the reconciliation), not by its text — so a reworded-but-same issue reads as
// carried, not as one resolved plus one new.
public sealed record DiffFinding(FindingChange Change, FindingCategory Category, string Chapter, string Detail);

// A scene that changed between drafts, paired with the previous draft's evidence image and the new one, so
// the author can see how the illustration shifted (the comprehension probe). Either image may be null when
// that draft's render was unavailable.
public sealed record DiffScenePair(string Citation, byte[]? PreviousImage, byte[]? NewImage);

// What changed between a draft and the draft it was made from: the report findings resolved, newly raised,
// and regressed (by finding identity), and the scenes that changed with their before/after evidence images.
// A first draft (no predecessor) diffs to None.
public sealed record DraftDiff(
    Guid DraftId,
    Guid? PreviousDraftId,
    IReadOnlyList<DiffFinding> ResolvedFindings,
    IReadOnlyList<DiffFinding> NewFindings,
    IReadOnlyList<DiffFinding> RegressedFindings,
    IReadOnlyList<DiffScenePair> ChangedScenes)
{
    public static DraftDiff None(Guid draftId) =>
        new(draftId, null, [], [], [], []);
}
