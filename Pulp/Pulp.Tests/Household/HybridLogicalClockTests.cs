using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

public class HybridLogicalClockTests
{
    [Fact]
    public void ALocalEventTakesThePhysicalTimeWhenItIsAhead()
    {
        var clock = new HybridLogicalClock();

        var stamp = clock.Now(1000);

        Assert.Equal(new HlcTimestamp(1000, 0), stamp);
    }

    [Fact]
    public void TwoEventsInTheSameMillisecondStillOrder()
    {
        var clock = new HybridLogicalClock();

        var first = clock.Now(1000);
        var second = clock.Now(1000);

        Assert.True(second > first);
        Assert.Equal(new HlcTimestamp(1000, 1), second);
    }

    [Fact]
    public void APhysicalClockThatStepsBackwardsNeverRewindsTheStamp()
    {
        var clock = new HybridLogicalClock();
        var before = clock.Now(5000);

        var after = clock.Now(4000);

        Assert.True(after > before, $"stamp went from {before} to {after} when the phone's clock stepped back");
    }

    [Fact]
    public void WitnessingARemoteStampFromTheFutureMovesTheClockPastIt()
    {
        var clock = new HybridLogicalClock();
        clock.Now(1000);
        var remote = new HlcTimestamp(9000, 3);

        clock.Witness(remote, 1100);
        var next = clock.Now(1200);

        Assert.True(next > remote, $"a write made after reading {remote} was stamped {next}, before it");
    }

    [Fact]
    public void WitnessingAStampFromThePastLeavesTheClockAhead()
    {
        var clock = new HybridLogicalClock();
        var mine = clock.Now(5000);

        clock.Witness(new HlcTimestamp(100, 7), 5000);

        Assert.True(clock.Last > mine);
        Assert.Equal(5000, clock.Last.WallMillis);
    }

    [Fact]
    public void StampsOrderByWallThenCounter()
    {
        Assert.True(new HlcTimestamp(2, 0) > new HlcTimestamp(1, 99));
        Assert.True(new HlcTimestamp(2, 1) > new HlcTimestamp(2, 0));
        Assert.Equal(0, new HlcTimestamp(2, 1).CompareTo(new HlcTimestamp(2, 1)));
    }

    [Fact]
    public void AClockCanResumeFromTheLastStampItWrote()
    {
        var clock = new HybridLogicalClock(new HlcTimestamp(7000, 2));

        Assert.True(clock.Now(10) > new HlcTimestamp(7000, 2));
    }

    [Fact]
    public void ANegativeStampIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HlcTimestamp(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HlcTimestamp(0, -1));
    }
}
