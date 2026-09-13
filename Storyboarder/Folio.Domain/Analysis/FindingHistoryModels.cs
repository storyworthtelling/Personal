using Folio.Domain.Model;

namespace Folio.Domain.Analysis;

// The inputs and output of cross-draft finding reconciliation — the step that gives a draft's freshly
// consolidated findings their durable identity by matching them against the prior draft's threads. The
// matching itself is a Claude call (findings are reworded between drafts, so "is this the same issue?"
// can't be answered by text equality); FindingReconciler then applies the model's decisions deterministically
// to produce the persisted occurrence rows and the resolved-thread set.

// A finding thread carried into the reconciliation from earlier drafts: its stable identity, the most recent
// occurrence's text (what the model compares against), the draft it first opened in (so a carried occurrence
// keeps "open since draft N"), and whether it was still open in the immediately-previous draft — a match to
// a thread that had been fixed reads as a regression rather than a plain carry-over.
public sealed record PriorFinding(
    Guid ThreadId,
    FindingCategory Category,
    string Chapter,
    string Detail,
    int FirstDraftNumber,
    bool OpenInPreviousDraft);

// One of this draft's freshly-consolidated findings, awaiting an identity. Index is its position in the list
// the model is asked to reconcile, and how the model refers back to it.
public sealed record CandidateFinding(int Index, FindingCategory Category, string Chapter, string Detail);

// The model's verdict for one candidate: the prior ThreadId it is the same issue as, or null when it is a
// genuinely new issue. A candidate the model omits is treated as new.
public sealed record FindingMatch(int Index, Guid? ThreadId);

// The whole reconciliation reply — one match per candidate (by Index). A blank/partial reply degrades to
// "everything is new", so reconciliation never blocks the report.
public sealed record FindingReconciliation(IReadOnlyList<FindingMatch> Matches)
{
    public static FindingReconciliation Empty { get; } = new(Array.Empty<FindingMatch>());
}

// The result of applying a reconciliation: the occurrence rows to persist for this draft, and the threads
// that were open in the previous draft but have no occurrence now — i.e. resolved this draft.
public sealed record ReconciliationOutcome(
    IReadOnlyList<Finding> Occurrences,
    IReadOnlyList<Guid> ResolvedThreadIds);
