using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class PodcastShelfTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static Episode Ep(string id, string feedId, int daysAfterT0) =>
        new(id, feedId, Title: $"Episode {id}", PublishedUtc: T0.AddDays(daysAfterT0));

    private static readonly Dictionary<string, string> Titles = new()
    {
        ["f1"] = "Show One",
        ["f2"] = "Show Two",
    };

    [Fact]
    public void DoneEpisodes_UnionsListenedAndCompleted()
    {
        var episodes = new[] { Ep("a", "f1", 0), Ep("b", "f1", 1) };
        var progress = new PlaybackProgressBook();
        progress.Record("b", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(40), T0);

        var done = PodcastShelf.DoneEpisodes(episodes, ["a"], progress);

        Assert.Contains("a", done); // legacy listened
        Assert.Contains("b", done); // progress complete
    }

    [Fact]
    public void Build_OneRowPerShowWithUnplayedCount()
    {
        var episodes = new[] { Ep("a", "f1", 0), Ep("b", "f1", 1), Ep("c", "f2", 0) };

        var shelf = PodcastShelf.Build(episodes, Titles, new HashSet<string>());

        Assert.Equal(2, shelf.Count);
        Assert.Equal(2, shelf.Single(show => show.FeedId == "f1").UnplayedCount);
        Assert.Equal("Show One", shelf.Single(show => show.FeedId == "f1").Title);
    }

    [Fact]
    public void Build_HidesShowsWithNothingUnplayed()
    {
        var episodes = new[] { Ep("a", "f1", 0), Ep("c", "f2", 0) };

        var shelf = PodcastShelf.Build(episodes, Titles, new HashSet<string> { "a" });

        Assert.Equal("f2", Assert.Single(shelf).FeedId);
    }

    [Fact]
    public void Build_OrdersByFreshestUnplayedEpisode()
    {
        // f2's newest unplayed (day 10) is fresher than f1's (day 2).
        var episodes = new[]
        {
            Ep("a", "f1", 0), Ep("b", "f1", 2),
            Ep("c", "f2", 1), Ep("d", "f2", 10),
        };

        var shelf = PodcastShelf.Build(episodes, Titles, new HashSet<string>());

        Assert.Equal(["f2", "f1"], shelf.Select(show => show.FeedId));
    }

    [Fact]
    public void Build_CountsOnlyUnplayedNotFinished()
    {
        var episodes = new[] { Ep("a", "f1", 0), Ep("b", "f1", 1), Ep("c", "f1", 2) };

        var shelf = PodcastShelf.Build(episodes, Titles, new HashSet<string> { "a" });

        Assert.Equal(2, Assert.Single(shelf).UnplayedCount);
    }

    [Fact]
    public void Build_FallsBackToFeedIdWhenTitleMissing()
    {
        var episodes = new[] { Ep("a", "unknownFeed", 0) };

        var shelf = PodcastShelf.Build(episodes, Titles, new HashSet<string>());

        Assert.Equal("unknownFeed", Assert.Single(shelf).Title);
    }
}
