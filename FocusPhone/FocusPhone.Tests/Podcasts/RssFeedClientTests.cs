using FocusPhone.Infrastructure.Podcasts;
using Xunit;

namespace FocusPhone.Tests.Podcasts;

public class RssFeedClientTests
{
    private const string FeedUrl = "https://feeds.example.com/show.rss";

    private const string SampleRss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
          <channel>
            <title>Example Show</title>
            <item>
              <title>Episode Two</title>
              <guid>ep-2</guid>
              <pubDate>Tue, 02 Jun 2026 10:00:00 GMT</pubDate>
              <enclosure url="https://cdn.example.com/ep2.mp3" type="audio/mpeg" length="1024"/>
              <itunes:duration>01:02:03</itunes:duration>
            </item>
            <item>
              <title>Episode One</title>
              <guid>ep-1</guid>
              <pubDate>Mon, 01 Jun 2026 10:00:00 GMT</pubDate>
              <enclosure url="https://cdn.example.com/ep1.mp3" type="audio/mpeg" length="1024"/>
              <itunes:duration>1800</itunes:duration>
            </item>
            <item>
              <title>No Audio Item</title>
              <guid>ep-0</guid>
              <pubDate>Sun, 31 May 2026 10:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

    private async Task<PodcastFeed> FetchSampleAsync()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(FeedUrl, SampleRss, "application/rss+xml");
        var client = new RssFeedClient(new HttpClient(handler));
        return await client.FetchAsync(new Uri(FeedUrl));
    }

    [Fact]
    public async Task Fetch_ParsesFeedTitle()
    {
        var feed = await FetchSampleAsync();

        Assert.Equal("Example Show", feed.Title);
    }

    [Fact]
    public async Task Fetch_ParsesEpisodesWithAudio_SkippingItemsWithoutEnclosure()
    {
        var feed = await FetchSampleAsync();

        Assert.Equal(2, feed.Episodes.Count);
        Assert.DoesNotContain(feed.Episodes, e => e.Id == "ep-0");
    }

    [Fact]
    public async Task Fetch_MapsEpisodeFields()
    {
        var feed = await FetchSampleAsync();

        var episode = feed.Episodes.Single(e => e.Id == "ep-2");
        Assert.Equal("Episode Two", episode.Title);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero), episode.PublishedUtc);
        Assert.Equal("https://cdn.example.com/ep2.mp3", episode.AudioUrl);
        Assert.Equal(FeedUrl, episode.FeedId);
    }

    [Fact]
    public async Task Fetch_ParsesColonSeparatedDuration()
    {
        var feed = await FetchSampleAsync();

        Assert.Equal(new TimeSpan(1, 2, 3), feed.Episodes.Single(e => e.Id == "ep-2").Duration);
    }

    [Fact]
    public async Task Fetch_ParsesPlainSecondsDuration()
    {
        var feed = await FetchSampleAsync();

        Assert.Equal(TimeSpan.FromSeconds(1800), feed.Episodes.Single(e => e.Id == "ep-1").Duration);
    }

    [Fact]
    public async Task Fetch_MissingGuid_FallsBackToEnclosureUrl()
    {
        const string rss = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item>
                <title>E</title>
                <pubDate>Mon, 01 Jun 2026 10:00:00 GMT</pubDate>
                <enclosure url="https://cdn.example.com/x.mp3" type="audio/mpeg"/>
              </item>
            </channel></rss>
            """;
        var handler = new FakeHttpMessageHandler();
        handler.Respond(FeedUrl, rss, "application/rss+xml");

        var feed = await new RssFeedClient(new HttpClient(handler)).FetchAsync(new Uri(FeedUrl));

        Assert.Equal("https://cdn.example.com/x.mp3", Assert.Single(feed.Episodes).Id);
    }

    [Fact]
    public async Task Fetch_HttpFailure_ThrowsWithFeedUrlInMessage()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new RssFeedClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.FetchAsync(new Uri("https://feeds.example.com/missing.rss")));
        Assert.Contains("missing.rss", ex.Message);
    }
}
