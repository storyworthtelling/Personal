using Codex.Domain.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class UpdateRunGateTests
{
    [Fact]
    public async Task TwoOverlappingRunsNeverExecuteAtTheSameTime()
    {
        var gate = new UpdateRunGate();
        var running = 0;
        var peak = 0;

        async Task<int> Run()
        {
            return await gate.RunAsync(async () =>
            {
                var now = Interlocked.Increment(ref running);
                peak = Math.Max(peak, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref running);
                return 1;
            });
        }

        await Task.WhenAll(Run(), Run(), Run());

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task AThrowingRunFreesTheGateAndTheExceptionReachesTheCaller()
    {
        var gate = new UpdateRunGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync<int>(() => throw new InvalidOperationException("boom")));

        Assert.Equal(7, await gate.RunAsync(() => Task.FromResult(7)));
    }

    // P14 release A fix round, finding 4: the watch half waits for the gate only as long as its run's deadline leaves.
    [Fact]
    public async Task AFreeGateRunsAtOnce()
    {
        var gate = new UpdateRunGate();
        var ran = false;

        Assert.True(await gate.TryRunAsync(() => { ran = true; return Task.CompletedTask; }, TimeSpan.Zero));
        Assert.True(ran);
    }

    [Fact]
    public async Task AWaitThatRunsOutRunsNothing()
    {
        var gate = new UpdateRunGate();
        var held = new TaskCompletionSource();
        var holding = gate.RunAsync(async () => { await held.Task; return 0; });
        var ran = false;

        Assert.False(await gate.TryRunAsync(() => { ran = true; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50)));
        Assert.False(ran);

        held.SetResult();
        await holding;
        Assert.True(await gate.TryRunAsync(() => { ran = true; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50)));
        Assert.True(ran);
    }

    [Fact]
    public async Task AGateHeldOnlyBrieflyIsWaitedFor()
    {
        var gate = new UpdateRunGate();
        var holding = gate.RunAsync(async () => { await Task.Delay(30); return 0; });

        Assert.True(await gate.TryRunAsync(() => Task.CompletedTask, TimeSpan.FromSeconds(10)));
        await holding;
    }

    [Fact]
    public async Task AThrowingTryFreesTheGateAndTheExceptionReachesTheCaller()
    {
        var gate = new UpdateRunGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.TryRunAsync(() => throw new InvalidOperationException("boom"), TimeSpan.Zero));

        Assert.True(await gate.TryRunAsync(() => Task.CompletedTask, TimeSpan.Zero));
    }

    [Fact]
    public async Task ANegativeWaitIsNoWait()
    {
        // A deadline already past leaves a negative wait: the gate is tried once, never waited on forever.
        var gate = new UpdateRunGate();
        var held = new TaskCompletionSource();
        var holding = gate.RunAsync(async () => { await held.Task; return 0; });

        Assert.False(await gate.TryRunAsync(() => Task.CompletedTask, TimeSpan.FromMinutes(-3)));
        held.SetResult();
        await holding;
    }
}

public class UpdateServiceUsesTheGateTests
{
    [Fact]
    public void EveryUpdateRunGoesThroughTheGate()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Flyleaf.Android", "UpdateService.cs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!, "Flyleaf.Android", "UpdateService.cs"));
        Assert.Contains("RunGate.RunAsync(() => RunAsync(context, force))", source);
    }
}
