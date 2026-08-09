using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class BulkListenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static Episode Ep(string id, int daysAfterT0, string feedId = "feed1") =>
        new(id, feedId, Title: $"Episode {id}", PublishedUtc: T0.AddDays(daysAfterT0));

    [Fact]
    public void AllInShow_ReturnsEveryEpisodeOfThatFeed()
    {
        Episode[] episodes = [Ep("a", 0), Ep("b", 1), Ep("other", 2, feedId: "feed2")];

        var ids = BulkListen.AllInShow(episodes, "feed1");

        Assert.Equal(["a", "b"], ids.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void AllInShow_UnknownFeed_ReturnsEmpty()
    {
        Episode[] episodes = [Ep("a", 0)];

        Assert.Empty(BulkListen.AllInShow(episodes, "missing"));
    }

    [Fact]
    public void OlderInShow_ReturnsStrictlyEarlierEpisodesOfSameFeed()
    {
        var anchor = Ep("mid", 5);
        Episode[] episodes = [Ep("old", 0), anchor, Ep("new", 10)];

        var ids = BulkListen.OlderInShow(episodes, anchor);

        Assert.Equal(["old"], ids.ToArray());
    }

    [Fact]
    public void OlderInShow_ExcludesAnchorItself()
    {
        var anchor = Ep("mid", 5);
        Episode[] episodes = [anchor];

        Assert.Empty(BulkListen.OlderInShow(episodes, anchor));
    }

    [Fact]
    public void OlderInShow_IgnoresOtherFeeds()
    {
        var anchor = Ep("mid", 5);
        Episode[] episodes = [anchor, Ep("otherOld", 0, feedId: "feed2")];

        Assert.Empty(BulkListen.OlderInShow(episodes, anchor));
    }

    [Fact]
    public void OlderInShow_SamePublishTime_IsNotOlder()
    {
        var anchor = Ep("anchor", 5);
        Episode[] episodes = [anchor, Ep("tie", 5)];

        Assert.Empty(BulkListen.OlderInShow(episodes, anchor));
    }
}
