using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Analysis;

// Applies a cross-draft reconciliation deterministically: given this draft's candidate findings and the
// model's decision of which prior thread each is the same issue as, it produces the occurrence rows to
// persist (with stable ThreadIds and per-draft status) and the set of threads resolved this draft. Pure —
// the model call lives in the Claude client — so the carry/regress/resolve rules are unit-tested against a
// faked reconciliation. The rules:
//   - matched to a thread that was OPEN in the previous draft  → Carried (same ThreadId, origin preserved)
//   - matched to a thread that had been FIXED (not open before) → Regressed (it came back)
//   - unmatched, or a match the model can't be trusted on       → New (a fresh thread)
//   - a prior thread open in the previous draft with no match   → Resolved this draft
public sealed class FindingReconciler
{
    // A first (or lone) draft: no prior threads, so every finding opens a new thread.
    public ReconciliationOutcome ReconcileFirstDraft(
        Guid novelId, Guid projectId, int draftNumber, IReadOnlyList<CandidateFinding> candidates) =>
        Reconcile(novelId, projectId, draftNumber, Array.Empty<PriorFinding>(), candidates, FindingReconciliation.Empty);

    public ReconciliationOutcome Reconcile(
        Guid novelId,
        Guid projectId,
        int draftNumber,
        IReadOnlyList<PriorFinding> priorThreads,
        IReadOnlyList<CandidateFinding> candidates,
        FindingReconciliation reconciliation)
    {
        var priorById = new Dictionary<Guid, PriorFinding>();
        foreach (var prior in priorThreads)
            priorById[prior.ThreadId] = prior;

        var matchByIndex = new Dictionary<int, Guid?>();
        foreach (var match in reconciliation.Matches)
            matchByIndex[match.Index] = match.ThreadId;

        var claimed = new HashSet<Guid>();
        var occurrences = new List<Finding>(candidates.Count);

        foreach (var candidate in candidates)
        {
            // The model's verdict is honored only when it names a real, not-yet-claimed prior thread; anything
            // else (no match, an unknown id, or a thread another candidate already claimed) becomes a new thread
            // so the reconciliation can never corrupt an identity or double-count one.
            var matched = matchByIndex.TryGetValue(candidate.Index, out var threadId)
                          && threadId is { } id
                          && priorById.TryGetValue(id, out _)
                          && claimed.Add(id)
                ? priorById[threadId!.Value]
                : null;

            occurrences.Add(matched is null
                ? new Finding
                {
                    NovelId = novelId,
                    ProjectId = projectId,
                    ThreadId = Guid.NewGuid(),
                    Category = candidate.Category,
                    Chapter = candidate.Chapter,
                    Detail = candidate.Detail,
                    Status = FindingStatus.New,
                    FirstDraftNumber = draftNumber
                }
                : new Finding
                {
                    NovelId = novelId,
                    ProjectId = projectId,
                    ThreadId = matched.ThreadId,
                    Category = candidate.Category,
                    Chapter = candidate.Chapter,
                    Detail = candidate.Detail,
                    Status = matched.OpenInPreviousDraft ? FindingStatus.Carried : FindingStatus.Regressed,
                    FirstDraftNumber = matched.FirstDraftNumber
                });
        }

        // Resolved this draft = threads that were open in the previous draft but no current finding claimed.
        var resolved = priorThreads
            .Where(prior => prior.OpenInPreviousDraft && !claimed.Contains(prior.ThreadId))
            .Select(prior => prior.ThreadId)
            .ToList();

        return new ReconciliationOutcome(occurrences, resolved);
    }
}
