using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// The background loop faulted silently on a Drive timeout (a TaskCanceledException carrying
/// CancellationToken.None) and sync never ran again for the process. The loop must outlive any one
/// pass, however it fails, and back off rather than spin.
/// </summary>
public class HouseholdSyncLoopTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static async Task<bool> Reached(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task ATimeoutThrownAsACancellationDoesNotKillTheLoop()
    {
        var passes = 0;
        var faults = new List<Exception>();
        var loop = new HouseholdSyncLoop(
            _ =>
            {
                passes++;
                if (passes == 1)
                {
                    throw new TaskCanceledException("The request timed out.");
                }

                return Task.CompletedTask;
            },
            faults.Add,
            () => Start);
        loop.Opened();
        using var stop = new CancellationTokenSource();

        var running = loop.RunAsync(stop.Token);
        Assert.True(await Reached(() => passes >= 1), "The first pass never ran.");
        Assert.True(await Reached(() => faults.Count == 1), "The failed pass was not reported.");

        loop.Ask();
        Assert.True(await Reached(() => passes >= 2), "The loop stopped after the timeout; sync never ran again.");
        Assert.False(running.IsFaulted);

        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task AFailedPassIsBackedOffNotRetriedInAHotLoop()
    {
        var passes = 0;
        var loop = new HouseholdSyncLoop(
            _ =>
            {
                passes++;
                throw new InvalidOperationException("boom");
            },
            _ => { },
            () => Start);
        loop.Opened();
        using var stop = new CancellationTokenSource();

        var running = loop.RunAsync(stop.Token);
        Assert.True(await Reached(() => passes >= 1));
        await Task.Delay(150);

        Assert.Equal(1, passes);
        stop.Cancel();
        await running;
    }

    [Fact]
    public async Task StoppingTheLoopWithItsOwnTokenEndsItQuietly()
    {
        var loop = new HouseholdSyncLoop(_ => Task.CompletedTask, _ => { }, () => Start);
        using var stop = new CancellationTokenSource();

        var running = loop.RunAsync(stop.Token);
        stop.Cancel();
        await running;

        Assert.True(running.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task APassTheScheduleHandedOutIsRunOnOpen()
    {
        var passes = 0;
        var loop = new HouseholdSyncLoop(_ => { passes++; return Task.CompletedTask; }, _ => { }, () => Start);
        loop.Opened();
        using var stop = new CancellationTokenSource();

        var running = loop.RunAsync(stop.Token);

        Assert.True(await Reached(() => passes >= 1));
        stop.Cancel();
        await running;
    }
}
