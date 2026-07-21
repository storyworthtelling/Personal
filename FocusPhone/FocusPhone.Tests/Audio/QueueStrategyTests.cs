using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class QueueStrategyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static Episode Ep(string id, string feedId, int daysAfterT0) =>
        new(id, feedId, Title: $"Episode {id}", PublishedUtc: T0.AddDays(daysAfterT0));

    // ---- Chronological ----

    [Fact]
    public void Chronological_OrdersAllUnplayedOldestFirst()
    {
        var episodes = new[] { Ep("a", "f1", 5), Ep("b", "f2", 1), Ep("c", "f1", 3) };

        var order = new ChronologicalStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["b", "c", "a"], order);
    }

    [Fact]
    public void Chronological_HandlesEmptyInput()
    {
        Assert.Empty(new ChronologicalStrategy().Arrange([]));
    }

    // ---- Round-robin ----

    [Fact]
    public void RoundRobin_AlternatesShowsOldestFirstWithinEachShow()
    {
        // f1: a(0), c(2), e(4)   f2: b(1), d(3)
        var episodes = new[]
        {
            Ep("a", "f1", 0), Ep("c", "f1", 2), Ep("e", "f1", 4),
            Ep("b", "f2", 1), Ep("d", "f2", 3),
        };

        var order = new RoundRobinStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        // f1 leads (its oldest episode is oldest overall); then alternate rounds.
        Assert.Equal(["a", "b", "c", "d", "e"], order);
    }

    [Fact]
    public void RoundRobin_ShowOrderIsByEachShowsOldestEpisode()
    {
        // f2's oldest (day 0) precedes f1's oldest (day 1), so f2 leads each round.
        var episodes = new[]
        {
            Ep("a", "f1", 1), Ep("c", "f1", 3),
            Ep("b", "f2", 0), Ep("d", "f2", 2),
        };

        var order = new RoundRobinStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["b", "a", "d", "c"], order);
    }

    [Fact]
    public void RoundRobin_DrainsLongerShowAfterShorterOnesExhaust()
    {
        var episodes = new[]
        {
            Ep("a", "f1", 0), Ep("b", "f1", 2), Ep("c", "f1", 4),
            Ep("x", "f2", 1),
        };

        var order = new RoundRobinStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["a", "x", "b", "c"], order);
    }

    [Fact]
    public void RoundRobin_SingleShowIsJustChronological()
    {
        var episodes = new[] { Ep("a", "f1", 2), Ep("b", "f1", 0), Ep("c", "f1", 1) };

        var order = new RoundRobinStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["b", "c", "a"], order);
    }

    // ---- Backlog-buster ----

    [Fact]
    public void BacklogBuster_FewestRemainingShowFirstEachShowInFull()
    {
        // f1 has 3 unplayed, f2 has 1 -> f2 first, then f1 oldest-first.
        var episodes = new[]
        {
            Ep("a", "f1", 0), Ep("b", "f1", 2), Ep("c", "f1", 4),
            Ep("x", "f2", 1),
        };

        var order = new BacklogBusterStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["x", "a", "b", "c"], order);
    }

    [Fact]
    public void BacklogBuster_TieOnCountBrokenByOldestEpisode()
    {
        // Both shows have 2 unplayed; f2's oldest (day 0) beats f1's oldest (day 1).
        var episodes = new[]
        {
            Ep("a", "f1", 1), Ep("b", "f1", 3),
            Ep("x", "f2", 0), Ep("y", "f2", 2),
        };

        var order = new BacklogBusterStrategy().Arrange(episodes).Select(e => e.Id).ToArray();

        Assert.Equal(["x", "y", "a", "b"], order);
    }

    [Fact]
    public void BacklogBuster_HandlesEmptyInput()
    {
        Assert.Empty(new BacklogBusterStrategy().Arrange([]));
    }

    // ---- Resolver ----

    [Theory]
    [InlineData(QueueArrangement.Chronological, typeof(ChronologicalStrategy))]
    [InlineData(QueueArrangement.RoundRobin, typeof(RoundRobinStrategy))]
    [InlineData(QueueArrangement.BacklogBuster, typeof(BacklogBusterStrategy))]
    public void Resolver_MapsArrangementToStrategy(QueueArrangement arrangement, Type expected)
    {
        Assert.IsType(expected, EpisodeQueueStrategy.For(arrangement));
    }

    [Fact]
    public void Resolver_ManualHasNoStrategy()
    {
        Assert.Null(EpisodeQueueStrategy.For(QueueArrangement.Manual));
    }
}
