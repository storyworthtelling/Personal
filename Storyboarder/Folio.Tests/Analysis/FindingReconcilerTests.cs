using Folio.Domain.Analysis;
using Folio.Domain.Model;
using Xunit;

namespace Folio.Tests.Analysis;

// The pure application of a reconciliation: turning a draft's candidate findings + the model's match
// decisions into persisted occurrence rows (with stable ThreadIds and per-draft status) and the set of
// threads resolved this draft. The Claude call that produces the matches is faked here.
public class FindingReconcilerTests
{
    private readonly FindingReconciler reconciler = new();
    private readonly Guid novelId = Guid.NewGuid();
    private readonly Guid projectId = Guid.NewGuid();

    private static CandidateFinding Candidate(int index, string detail, FindingCategory category = FindingCategory.PlotHole) =>
        new(index, category, "Chapter 1", detail);

    [Fact]
    public void FirstDraft_EveryFindingIsANewThread()
    {
        var candidates = new[] { Candidate(0, "The gate opens unexplained"), Candidate(1, "Eye colour shifts") };

        var outcome = reconciler.ReconcileFirstDraft(novelId, projectId, draftNumber: 1, candidates);

        Assert.Equal(2, outcome.Occurrences.Count);
        Assert.All(outcome.Occurrences, f =>
        {
            Assert.Equal(FindingStatus.New, f.Status);
            Assert.Equal(1, f.FirstDraftNumber);
            Assert.Equal(novelId, f.NovelId);
            Assert.Equal(projectId, f.ProjectId);
        });
        // Distinct thread identities.
        Assert.Equal(2, outcome.Occurrences.Select(f => f.ThreadId).Distinct().Count());
        Assert.Empty(outcome.ResolvedThreadIds);
    }

    [Fact]
    public void MatchedOpenThread_IsCarried_KeepingItsIdentityAndOrigin()
    {
        var thread = Guid.NewGuid();
        var prior = new[]
        {
            new PriorFinding(thread, FindingCategory.PlotHole, "Chapter 1", "The gate opens with no cause",
                FirstDraftNumber: 1, OpenInPreviousDraft: true)
        };
        var candidates = new[] { Candidate(0, "The gate still opens unexplained") };
        var matches = new FindingReconciliation([new FindingMatch(0, thread)]);

        var outcome = reconciler.Reconcile(novelId, projectId, draftNumber: 2, prior, candidates, matches);

        var f = Assert.Single(outcome.Occurrences);
        Assert.Equal(thread, f.ThreadId);
        Assert.Equal(FindingStatus.Carried, f.Status);
        Assert.Equal(1, f.FirstDraftNumber);           // origin preserved across the carry
        Assert.Empty(outcome.ResolvedThreadIds);
    }

    [Fact]
    public void PriorOpenThread_WithNoMatch_IsResolved()
    {
        var open = Guid.NewGuid();
        var prior = new[]
        {
            new PriorFinding(open, FindingCategory.Pacing, "Chapter 5", "The middle sags",
                FirstDraftNumber: 1, OpenInPreviousDraft: true)
        };
        var candidates = new[] { Candidate(0, "A brand-new hole", FindingCategory.PlotHole) };
        var matches = new FindingReconciliation([new FindingMatch(0, null)]);

        var outcome = reconciler.Reconcile(novelId, projectId, draftNumber: 2, prior, candidates, matches);

        Assert.Equal(open, Assert.Single(outcome.ResolvedThreadIds));
        var f = Assert.Single(outcome.Occurrences);
        Assert.Equal(FindingStatus.New, f.Status);      // the candidate is a new thread
        Assert.NotEqual(open, f.ThreadId);
    }

    [Fact]
    public void MatchToAPreviouslyFixedThread_IsARegression()
    {
        // The thread existed but was fixed (not open in the previous draft); a fix elsewhere brought it back.
        var thread = Guid.NewGuid();
        var prior = new[]
        {
            new PriorFinding(thread, FindingCategory.Inconsistency, "Chapter 9", "Eye colour shifts",
                FirstDraftNumber: 1, OpenInPreviousDraft: false)
        };
        var candidates = new[] { Candidate(0, "Eye colour shifts again", FindingCategory.Inconsistency) };
        var matches = new FindingReconciliation([new FindingMatch(0, thread)]);

        var outcome = reconciler.Reconcile(novelId, projectId, draftNumber: 3, prior, candidates, matches);

        var f = Assert.Single(outcome.Occurrences);
        Assert.Equal(thread, f.ThreadId);
        Assert.Equal(FindingStatus.Regressed, f.Status);
        // A thread fixed in the previous draft is not "resolved this draft" — it reappeared.
        Assert.Empty(outcome.ResolvedThreadIds);
    }

    [Fact]
    public void UnknownThreadId_OrDuplicateMatch_DegradesToNew()
    {
        var thread = Guid.NewGuid();
        var prior = new[]
        {
            new PriorFinding(thread, FindingCategory.PlotHole, "Chapter 1", "The gate opens unexplained",
                FirstDraftNumber: 1, OpenInPreviousDraft: true)
        };
        var candidates = new[]
        {
            Candidate(0, "The gate opens unexplained"),        // legitimately matches the thread
            Candidate(1, "The gate opens unexplained, part 2"), // model wrongly maps to the SAME thread
            Candidate(2, "A ghost id")                          // model returns a thread that doesn't exist
        };
        var matches = new FindingReconciliation(
        [
            new FindingMatch(0, thread),
            new FindingMatch(1, thread),
            new FindingMatch(2, Guid.NewGuid())
        ]);

        var outcome = reconciler.Reconcile(novelId, projectId, draftNumber: 2, prior, candidates, matches);

        // Candidate 0 claims the thread (Carried); 1 and 2 fall back to fresh new threads.
        var byDetail = outcome.Occurrences.ToDictionary(f => f.Detail);
        Assert.Equal(FindingStatus.Carried, byDetail["The gate opens unexplained"].Status);
        Assert.Equal(thread, byDetail["The gate opens unexplained"].ThreadId);
        Assert.Equal(FindingStatus.New, byDetail["The gate opens unexplained, part 2"].Status);
        Assert.NotEqual(thread, byDetail["The gate opens unexplained, part 2"].ThreadId);
        Assert.Equal(FindingStatus.New, byDetail["A ghost id"].Status);
        // The thread was claimed, so it is not resolved.
        Assert.Empty(outcome.ResolvedThreadIds);
    }

    [Fact]
    public void OmittedCandidate_IsTreatedAsNew()
    {
        var prior = Array.Empty<PriorFinding>();
        var candidates = new[] { Candidate(0, "First"), Candidate(1, "Second") };
        // The model only returned a match for candidate 0; candidate 1 is missing entirely.
        var matches = new FindingReconciliation([new FindingMatch(0, null)]);

        var outcome = reconciler.Reconcile(novelId, projectId, draftNumber: 2, prior, candidates, matches);

        Assert.Equal(2, outcome.Occurrences.Count);
        Assert.All(outcome.Occurrences, f => Assert.Equal(FindingStatus.New, f.Status));
    }
}
