using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// Two behaviours the copied Pulp tests did not pin, found by changing the engine and watching nothing
/// fail. Added after the engine was green, so they were not red first.
/// </summary>
public class MutationGapTests
{
    private static async Task<bool> Reached(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public void WitnessingTheSameMomentMovesPastBothStamps()
    {
        var clock = new HybridLogicalClock(new HlcTimestamp(1000, 3));
        var remote = new HlcTimestamp(1000, 5);

        var after = clock.Witness(remote, 1000);

        Assert.True(after > remote);
        Assert.True(after > new HlcTimestamp(1000, 3));
    }

    [Fact]
    public async Task AFailedPassIsRetriedOnceTheBackoffHasPassed()
    {
        var now = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var passes = 0;
        var faults = 0;
        var loop = new HouseholdSyncLoop(
            _ =>
            {
                passes++;
                return passes == 1 ? throw new InvalidOperationException("Drive was unreachable.") : Task.CompletedTask;
            },
            _ => faults++,
            () => now);
        loop.Opened();
        using var stop = new CancellationTokenSource();

        var running = loop.RunAsync(stop.Token);
        Assert.True(await Reached(() => faults == 1));

        now += HouseholdSyncSchedule.FirstRetry;
        loop.Wake();

        Assert.True(await Reached(() => passes >= 2), "The failed pass was never put back on the schedule.");
        stop.Cancel();
        await running;
    }
}
