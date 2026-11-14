using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Podcasts;

// Efficiency audit §9: a feed is asked again at most once an hour, and the episode cache carries each feed's
// validators so the next ask can be conditional.
public class FeedRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANeverFetchedFeedIsDue() => Assert.True(FeedRefresh.Due(null, Now));

    [Fact]
    public void AFeedFetchedWithinTheHourIsNotDue() =>
        Assert.False(FeedRefresh.Due(new FeedState(null, null, Now.AddMinutes(-30)), Now));

    [Fact]
    public void AFeedFetchedAnHourAgoIsDue() =>
        Assert.True(FeedRefresh.Due(new FeedState(null, null, Now - FeedRefresh.Interval), Now));

    [Fact]
    public void AClockThatWentBackMakesItDue() =>
        Assert.True(FeedRefresh.Due(new FeedState(null, null, Now.AddHours(1)), Now));

    [Fact]
    public void TheCacheKeepsEachFeedsState()
    {
        var cache = new PodcastCache([], new Dictionary<string, string> { ["https://feeds.example.com/a"] = "A" })
        {
            FeedStates = new Dictionary<string, FeedState> { ["https://feeds.example.com/a"] = new("\"e1\"", null, Now) },
        };

        var back = PodcastCacheCodec.Decode(PodcastCacheCodec.Encode(cache));

        Assert.Equal("\"e1\"", back.FeedStates["https://feeds.example.com/a"].ETag);
    }

    [Fact]
    public void ACacheWrittenBeforeStatesReadsWithNone()
    {
        var back = PodcastCacheCodec.Decode("""{"Episodes":[],"FeedTitles":{"https://feeds.example.com/a":"A"}}""");

        Assert.Empty(back.FeedStates);
        Assert.Equal("A", back.FeedTitles["https://feeds.example.com/a"]);
    }
}
