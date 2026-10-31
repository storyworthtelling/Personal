using Constitution.Infrastructure.Workouts;

namespace Constitution.Tests.CrossRun;

/// <summary>
/// The one door the workout run, the meal run and any other placement writer go through, one at a time: both load the shared record
/// of written keys, write the calendar, then save the record, so two at once would lose one run's keys. Fair (first come, first
/// served), bounded (a wait that runs out is a typed "busy", not a hang) and honest (a run that fails frees the gate and the failure
/// reaches the caller).
/// </summary>
public class RunGateTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);

    private static T Ran<T>(GateResult<T> result) => Assert.IsType<GateResult<T>.Ran>(result).Outcome;

    [Fact]
    public async Task ARunOnAFreeGateRunsAndGivesItsOutcome()
    {
        var gate = new RunGate();

        var result = await gate.RunAsync(_ => Task.FromResult(41 + 1), Plenty, CancellationToken.None);

        Assert.Equal(42, Ran(result));
    }

    [Fact]
    public async Task TwoRunsAtOnceTakeTurns()
    {
        var gate = new RunGate();
        var inside = 0;
        var most = 0;
        var finished = 0;

        async Task<int> Work(CancellationToken cancellation)
        {
            var now = Interlocked.Increment(ref inside);
            most = Math.Max(most, now);
            await Task.Delay(20, cancellation);
            Interlocked.Decrement(ref inside);
            return Interlocked.Increment(ref finished);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => gate.RunAsync(Work, Plenty, CancellationToken.None)));

        Assert.Equal(1, most);
        Assert.Equal(8, results.Count(result => result is GateResult<int>.Ran));
    }

    [Fact]
    public async Task TheyAreServedInTheOrderTheyArrived()
    {
        var gate = new RunGate();
        var release = new TaskCompletionSource();
        var order = new List<int>();
        var first = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 0;
        }, Plenty, CancellationToken.None);

        var waiting = new List<Task<GateResult<int>>>();
        for (var turn = 1; turn <= 6; turn++)
        {
            var mine = turn;
            waiting.Add(gate.RunAsync(_ =>
            {
                lock (order)
                {
                    order.Add(mine);
                }

                return Task.FromResult(mine);
            }, Plenty, CancellationToken.None));
            await Task.Delay(5);
        }

        release.SetResult();
        await first;
        await Task.WhenAll(waiting);

        Assert.Equal([1, 2, 3, 4, 5, 6], order);
    }

    [Fact]
    public async Task AWaitThatRunsOutIsBusy()
    {
        var gate = new RunGate();
        var release = new TaskCompletionSource();
        var holder = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 1;
        }, Plenty, CancellationToken.None);
        var started = false;

        var result = await gate.RunAsync(_ =>
        {
            started = true;
            return Task.FromResult(2);
        }, TimeSpan.FromMilliseconds(40), CancellationToken.None);

        var busy = Assert.IsType<GateResult<int>.Busy>(result);
        Assert.False(started);
        Assert.False(string.IsNullOrWhiteSpace(busy.Sentence));
        release.SetResult();
        Assert.Equal(1, Ran(await holder));
    }

    [Fact]
    public async Task AGiveUpFreesItsPlaceInTheQueue()
    {
        var gate = new RunGate();
        var release = new TaskCompletionSource();
        var holder = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 1;
        }, Plenty, CancellationToken.None);
        var gaveUp = await gate.RunAsync(_ => Task.FromResult(2), TimeSpan.FromMilliseconds(30), CancellationToken.None);
        var patient = gate.RunAsync(_ => Task.FromResult(3), Plenty, CancellationToken.None);

        release.SetResult();

        Assert.IsType<GateResult<int>.Busy>(gaveUp);
        Assert.Equal(1, Ran(await holder));
        Assert.Equal(3, Ran(await patient));
    }

    [Fact]
    public async Task NoWaitMeansBusyWhileHeldAndRunsWhenFree()
    {
        var gate = new RunGate();
        var release = new TaskCompletionSource();
        var holder = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 1;
        }, Plenty, CancellationToken.None);

        Assert.IsType<GateResult<int>.Busy>(await gate.RunAsync(_ => Task.FromResult(2), TimeSpan.Zero, CancellationToken.None));
        release.SetResult();
        await holder;
        Assert.Equal(3, Ran(await gate.RunAsync(_ => Task.FromResult(3), TimeSpan.Zero, CancellationToken.None)));
    }

    [Fact]
    public async Task AFailingRunFreesTheGate()
    {
        var gate = new RunGate();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync<int>(_ => throw new InvalidOperationException("The run could not read the calendar."), Plenty, CancellationToken.None));

        Assert.Equal("The run could not read the calendar.", failure.Message);
        Assert.NotNull(failure.StackTrace);
        Assert.Equal(7, Ran(await gate.RunAsync(_ => Task.FromResult(7), TimeSpan.Zero, CancellationToken.None)));
    }

    [Fact]
    public async Task ARunThatFailsLaterFreesTheGateToo()
    {
        var gate = new RunGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync<int>(async _ =>
        {
            await Task.Delay(5);
            throw new InvalidOperationException("Late failure.");
        }, Plenty, CancellationToken.None));

        Assert.Equal(7, Ran(await gate.RunAsync(_ => Task.FromResult(7), TimeSpan.Zero, CancellationToken.None)));
    }

    [Fact]
    public async Task CancellingLeavesTheQueue()
    {
        var gate = new RunGate();
        var release = new TaskCompletionSource();
        var holder = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 1;
        }, Plenty, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var cancelled = gate.RunAsync(_ => Task.FromResult(2), Plenty, cancel.Token);
        var after = gate.RunAsync(_ => Task.FromResult(3), Plenty, CancellationToken.None);
        await Task.Delay(20);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();
        await holder;
        Assert.Equal(3, Ran(await after));
    }

    [Fact]
    public async Task AlreadyCancelledNeverRuns()
    {
        var gate = new RunGate();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var started = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.RunAsync(_ =>
        {
            started = true;
            return Task.FromResult(1);
        }, Plenty, cancel.Token));

        Assert.False(started);
        Assert.Equal(5, Ran(await gate.RunAsync(_ => Task.FromResult(5), TimeSpan.Zero, CancellationToken.None)));
    }

    [Fact]
    public async Task ANegativeWaitIsRefused()
    {
        var gate = new RunGate();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gate.RunAsync(_ => Task.FromResult(1), TimeSpan.FromSeconds(-1), CancellationToken.None));
    }

    [Fact]
    public async Task AStuckRunCannotHoldOthersForever()
    {
        var gate = new RunGate();
        var never = new TaskCompletionSource<int>();
        _ = gate.RunAsync(_ => never.Task, Plenty, CancellationToken.None);

        var result = await gate.RunAsync(_ => Task.FromResult(2), TimeSpan.FromMilliseconds(30), CancellationToken.None);

        // No deadlock: the stuck run holds the gate, and every other caller is told so within its wait.
        Assert.IsType<GateResult<int>.Busy>(result);
        never.SetResult(0);
    }

    [Fact]
    public async Task GiveUpRacingHandOverKeepsOneGate()
    {
        var gate = new RunGate();
        var inside = 0;
        var most = 0;
        var ran = 0;
        var busy = 0;

        async Task Caller(int index)
        {
            var wait = TimeSpan.FromMilliseconds(index % 5);
            var result = await gate.RunAsync(async _ =>
            {
                var now = Interlocked.Increment(ref inside);
                lock (gate)
                {
                    most = Math.Max(most, now);
                }

                await Task.Yield();
                Interlocked.Decrement(ref inside);
                return index;
            }, wait, CancellationToken.None);
            if (result is GateResult<int>.Ran)
            {
                Interlocked.Increment(ref ran);
            }
            else
            {
                Interlocked.Increment(ref busy);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 400).Select(index => Task.Run(() => Caller(index))));

        Assert.Equal(1, most);
        Assert.Equal(400, ran + busy);
        Assert.True(ran > 0);
        // And the gate is free at the end: a caller who will not wait still gets in.
        Assert.Equal(9, Ran(await gate.RunAsync(_ => Task.FromResult(9), TimeSpan.Zero, CancellationToken.None)));
    }

    [Fact]
    public void ThereIsOneSharedGateForTheProcess()
    {
        Assert.Same(RunGate.Shared, RunGate.Shared);
        Assert.True(RunGate.DefaultWait > TimeSpan.Zero);
        Assert.True(RunGate.DefaultWait <= TimeSpan.FromMinutes(10));
    }
}
