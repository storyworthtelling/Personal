using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class StandingQueueTests
{
    private static Episode Ep(string id, string feed, int day) =>
        new(id, feed, id, new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero),
            AudioUrl: $"http://a/{id}.mp3");

    private static readonly Episode A1 = Ep("a1", "A", 1);
    private static readonly Episode A2 = Ep("a2", "A", 3);
    private static readonly Episode B1 = Ep("b1", "B", 2);
    private static readonly Episode B2 = Ep("b2", "B", 4);

    [Fact]
    public void Rebuild_OrdersEverythingUnplayedByTheArrangement()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Chronological, [], [A2, B1, A1, B2]);

        Assert.Equal(["a1", "b1", "a2", "b2"], queue);
    }

    // The bug this exists to fix: the arrangement used to be applied once, so an
    // episode that arrived afterwards never joined the queue.
    [Fact]
    public void Rebuild_TakesInEpisodesThatArrivedSinceLastTime()
    {
        var before = StandingQueue.Rebuild(QueueArrangement.Chronological, [], [A1, B1]);
        var after = StandingQueue.Rebuild(QueueArrangement.Chronological, [], [A1, B1, A2]);

        Assert.Equal(["a1", "b1"], before);
        Assert.Equal(["a1", "b1", "a2"], after);
    }

    [Fact]
    public void Rebuild_DropsEpisodesThatHaveBeenPlayed()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Chronological, [], [B1, A2]);

        Assert.Equal(["b1", "a2"], queue);
    }

    // The other half of the bug: auto-arrange used to wipe the queue outright, so
    // anything queued by hand was lost the next time it ran.
    [Fact]
    public void Rebuild_KeepsHandAddedEpisodesAndPutsThemFirst()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Chronological, ["b2"], [A1, A2, B1, B2]);

        Assert.Equal(["b2", "a1", "b1", "a2"], queue);
    }

    [Fact]
    public void Rebuild_KeepsHandAddedEpisodesInThePinnedOrder()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Chronological, ["b2", "a2"], [A1, A2, B1, B2]);

        Assert.Equal(["b2", "a2", "a1", "b1"], queue);
    }

    [Fact]
    public void Rebuild_NeverListsAPinnedEpisodeTwice()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.RoundRobin, ["a1"], [A1, A2, B1, B2]);

        Assert.Equal(queue.Count, queue.Distinct().Count());
        Assert.Equal("a1", queue[0]);
    }

    [Fact]
    public void Rebuild_ForgetsAPinnedEpisodeOnceItHasBeenPlayed()
    {
        // b2 is pinned but no longer in the unplayed pool.
        var queue = StandingQueue.Rebuild(QueueArrangement.Chronological, ["b2"], [A1, B1]);

        Assert.Equal(["a1", "b1"], queue);
    }

    // Manual is the off switch: the queue is exactly what was put in it by hand,
    // and nothing arranges anything.
    [Fact]
    public void Rebuild_UnderManualKeepsOnlyWhatWasAddedByHand()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Manual, ["b1", "a2"], [A1, A2, B1, B2]);

        Assert.Equal(["b1", "a2"], queue);
    }

    [Fact]
    public void Rebuild_UnderManualStillDropsWhatHasBeenPlayed()
    {
        var queue = StandingQueue.Rebuild(QueueArrangement.Manual, ["b1", "a2"], [A2]);

        Assert.Equal(["a2"], queue);
    }

    [Fact]
    public void Rebuild_SurvivesAnEmptyPool()
    {
        Assert.Empty(StandingQueue.Rebuild(QueueArrangement.RoundRobin, ["a1"], []));
        Assert.Empty(StandingQueue.Rebuild(QueueArrangement.Manual, [], []));
    }
}
