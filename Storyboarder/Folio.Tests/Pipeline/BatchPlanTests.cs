using Folio.Domain.Pipeline;

namespace Folio.Tests.Pipeline;

/// <summary>
/// When unattended calls go to the batch tier (owner's ruling, R39: half the cost, results within a day)
/// and when they go one at a time. One batch per kind per draft; whatever a batch leaves undone goes one
/// at a time, so a failed item is retried under the ordinary attempt rules rather than resubmitted forever.
/// </summary>
public class BatchPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingWaitingMeansNothingToBatch() =>
        Assert.Equal(BatchStep.OneByOne, BatchPlan.Decide(0, null, Now).Step);

    [Fact]
    public void ASingleItemIsNotWorthABatch() =>
        Assert.Equal(BatchStep.OneByOne, BatchPlan.Decide(1, null, Now).Step);

    [Fact]
    public void TwoOrMoreWaitingWithNoBatchYetAreSubmitted() =>
        Assert.Equal(BatchStep.Submit, BatchPlan.Decide(2, null, Now).Step);

    [Fact]
    public void ABatchNeverPolledIsPolled() =>
        Assert.Equal(BatchStep.Poll, BatchPlan.Decide(5, new BatchState(Now.AddMinutes(-2), null, null), Now).Step);

    [Fact]
    public void ABatchPolledAMomentAgoWaitsOutTheInterval()
    {
        var (step, wait) = BatchPlan.Decide(5, new BatchState(Now.AddMinutes(-10), Now.AddSeconds(-20), null), Now);

        Assert.Equal(BatchStep.Wait, step);
        Assert.Equal(BatchPlan.PollEvery - TimeSpan.FromSeconds(20), wait);
    }

    [Fact]
    public void ABatchPolledLongEnoughAgoIsPolledAgain() =>
        Assert.Equal(BatchStep.Poll,
            BatchPlan.Decide(5, new BatchState(Now.AddMinutes(-10), Now - BatchPlan.PollEvery, null), Now).Step);

    /// <summary>After one batch, the leftovers go one at a time; a kind is never batched twice for a draft.</summary>
    [Fact]
    public void AFinishedBatchLeavesTheRestToOneByOne() =>
        Assert.Equal(BatchStep.OneByOne,
            BatchPlan.Decide(3, new BatchState(Now.AddHours(-1), Now.AddMinutes(-1), Now.AddMinutes(-1)), Now).Step);

    /// <summary>The batch window is 24 hours; past it the batch is abandoned and the items go one at a time.</summary>
    [Fact]
    public void ABatchPastItsWindowIsAbandoned() =>
        Assert.Equal(BatchStep.Abandon,
            BatchPlan.Decide(3, new BatchState(Now - BatchPlan.GiveUpAfter - TimeSpan.FromMinutes(1), Now.AddMinutes(-5), null), Now).Step);
}
