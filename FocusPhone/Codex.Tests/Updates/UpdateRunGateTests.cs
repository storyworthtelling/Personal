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
