using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// A screen's household writes land in the order they were made. An undo made while its act is still
/// being written must wait for it: run side by side, the undo can take the household gate first, get
/// the earlier clock stamp, and lose to the act it reverses — a removal from Continue watching that
/// sticks after its undo.
/// </summary>
public class HouseholdWriteQueueTests
{
    [Fact]
    public async Task AnUndoAfterRemovingFromContinueWatchingLandsAfterItsAct()
    {
        var landed = new List<string>();
        var actMayFinish = new TaskCompletionSource();
        var queue = new HouseholdWriteQueue((_, _) => { }, () => { });

        _ = queue.Enqueue("remove from Continue watching", async _ =>
        {
            await actMayFinish.Task;
            lock (landed)
            {
                landed.Add("remove");
            }
        });
        var tail = queue.Enqueue("put back on Continue watching", _ =>
        {
            lock (landed)
            {
                landed.Add("undo");
            }

            return Task.CompletedTask;
        });

        await Task.Delay(50);
        actMayFinish.SetResult();
        await tail;

        Assert.Equal(["remove", "undo"], landed);
    }

    [Fact]
    public async Task AFailedWriteIsReportedAndTheNextStillRuns()
    {
        var failures = new List<string>();
        var afters = 0;
        var ran = false;
        var queue = new HouseholdWriteQueue((what, _) => failures.Add(what), () => Interlocked.Increment(ref afters));

        _ = queue.Enqueue("add to a playlist", _ => throw new IOException("The household file is locked (example)."));
        await queue.Enqueue("put back on a playlist", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.Equal(["add to a playlist"], failures);
        Assert.True(ran);
        Assert.Equal(2, afters);
    }
}
