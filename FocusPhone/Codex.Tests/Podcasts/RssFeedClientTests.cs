using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Podcasts;

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

    [Fact]
    public async Task Fetch_NonXmlBody_ThrowsInvalidDataException()
    {
        // A feed URL that answers 200 with an HTML error/redirect page (common
        // when a feed has moved) must not leak a raw XmlException — the refresh
        // loop only expects the network/parse family, and an escaping XmlException
        // crashes the app and wedges the shelf on "Fetching episodes…".
        var handler = new FakeHttpMessageHandler();
        handler.Respond(FeedUrl, "<html><meta charset=\"utf-8\"><body>Not here &amp yet</body></html>", "text/html");
        var client = new RssFeedClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.FetchAsync(new Uri(FeedUrl)));
        Assert.Contains(FeedUrl, ex.Message);
    }

    // ---- Where the audio URL comes from ----
    //
    // The live bug: an episode whose audio is not in a bare <enclosure url> was
    // returned as null by ParseItem and then silently dropped, so the show
    // appeared on the shelf with nothing playable in it.

    private static async Task<PodcastFeed> FetchAsync(string body)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(FeedUrl, body, "application/rss+xml");
        return await new RssFeedClient(new HttpClient(handler)).FetchAsync(new Uri(FeedUrl));
    }

    [Fact]
    public async Task Fetch_MediaRssContent_IsPlayable()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
              <channel><title>S</title>
                <item>
                  <title>E</title><guid>ep-1</guid>
                  <media:content url="https://cdn.example.com/mrss.mp3" type="audio/mpeg"/>
                </item>
              </channel>
            </rss>
            """);

        Assert.Equal("https://cdn.example.com/mrss.mp3", Assert.Single(feed.Episodes).AudioUrl);
    }

    [Fact]
    public async Task Fetch_MediaRssGroup_IsPlayable()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
              <channel><title>S</title>
                <item>
                  <title>E</title><guid>ep-1</guid>
                  <media:group>
                    <media:content url="https://cdn.example.com/poster.jpg" type="image/jpeg"/>
                    <media:content url="https://cdn.example.com/grouped.mp3" medium="audio"/>
                  </media:group>
                </item>
              </channel>
            </rss>
            """);

        Assert.Equal("https://cdn.example.com/grouped.mp3", Assert.Single(feed.Episodes).AudioUrl);
    }

    [Fact]
    public async Task Fetch_MultipleEnclosures_PrefersTheAudioOne()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item>
                <title>E</title><guid>ep-1</guid>
                <enclosure url="https://cdn.example.com/transcript.pdf" type="application/pdf"/>
                <enclosure url="https://cdn.example.com/real.mp3" type="audio/mpeg"/>
              </item>
            </channel></rss>
            """);

        Assert.Equal("https://cdn.example.com/real.mp3", Assert.Single(feed.Episodes).AudioUrl);
    }

    // A great many feeds omit the type attribute entirely; a typeless enclosure
    // is still the episode, so it must not be rejected for lack of a label.
    [Fact]
    public async Task Fetch_TypelessEnclosure_IsPlayable()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item><title>E</title><guid>ep-1</guid>
                <enclosure url="https://cdn.example.com/untyped.mp3"/></item>
            </channel></rss>
            """);

        Assert.Equal("https://cdn.example.com/untyped.mp3", Assert.Single(feed.Episodes).AudioUrl);
    }

    [Fact]
    public async Task Fetch_AtomFeed_IsParsed()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom Show</title>
              <entry>
                <title>Atom Episode</title>
                <id>atom-1</id>
                <published>2026-06-02T10:00:00Z</published>
                <link rel="enclosure" type="audio/mpeg" href="https://cdn.example.com/atom.mp3"/>
              </entry>
            </feed>
            """);

        Assert.Equal("Atom Show", feed.Title);
        var episode = Assert.Single(feed.Episodes);
        Assert.Equal("atom-1", episode.Id);
        Assert.Equal("Atom Episode", episode.Title);
        Assert.Equal("https://cdn.example.com/atom.mp3", episode.AudioUrl);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero), episode.PublishedUtc);
    }

    [Fact]
    public async Task Fetch_ItemWithNoAudioAnywhere_IsStillSkipped()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item><title>Just a note</title><guid>ep-0</guid></item>
            </channel></rss>
            """);

        Assert.Empty(feed.Episodes);
    }

    // ---- Notes and artwork ----
    //
    // Neither was parsed at all, so Now Playing's Notes panel had nothing to show
    // and every show fell back to the bookplate even when it published art.

    [Fact]
    public async Task Fetch_ReadsDescriptionAsPlainText()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item><title>E</title><guid>ep-1</guid>
                <description>&lt;p&gt;Four minutes across.&lt;/p&gt;&lt;p&gt;Two hundred years old.&lt;/p&gt;</description>
                <enclosure url="https://cdn.example.com/a.mp3"/></item>
            </channel></rss>
            """);

        Assert.Equal("Four minutes across.\nTwo hundred years old.",
            Assert.Single(feed.Episodes).Description);
    }

    [Fact]
    public async Task Fetch_FallsBackToItunesSummaryThenContentEncoded()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"
                 xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd"
                 xmlns:content="http://purl.org/rss/1.0/modules/content/">
              <channel><title>S</title>
                <item><title>A</title><guid>ep-1</guid>
                  <itunes:summary>From the summary.</itunes:summary>
                  <content:encoded>From content encoded.</content:encoded>
                  <enclosure url="https://cdn.example.com/a.mp3"/></item>
                <item><title>B</title><guid>ep-2</guid>
                  <content:encoded>Only content encoded.</content:encoded>
                  <enclosure url="https://cdn.example.com/b.mp3"/></item>
              </channel>
            </rss>
            """);

        Assert.Equal("From the summary.", feed.Episodes.Single(e => e.Id == "ep-1").Description);
        Assert.Equal("Only content encoded.", feed.Episodes.Single(e => e.Id == "ep-2").Description);
    }

    [Fact]
    public async Task Fetch_ReadsItemArtwork_FallingBackToTheChannels()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
              <channel><title>S</title>
                <itunes:image href="https://cdn.example.com/show.jpg"/>
                <item><title>A</title><guid>ep-1</guid>
                  <itunes:image href="https://cdn.example.com/ep1.jpg"/>
                  <enclosure url="https://cdn.example.com/a.mp3"/></item>
                <item><title>B</title><guid>ep-2</guid>
                  <enclosure url="https://cdn.example.com/b.mp3"/></item>
              </channel>
            </rss>
            """);

        Assert.Equal("https://cdn.example.com/ep1.jpg", feed.Episodes.Single(e => e.Id == "ep-1").ImageUrl);
        Assert.Equal("https://cdn.example.com/show.jpg", feed.Episodes.Single(e => e.Id == "ep-2").ImageUrl);
    }

    [Fact]
    public async Task Fetch_FallsBackToChannelImageUrlElement()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <image><url>https://cdn.example.com/legacy.png</url></image>
              <item><title>A</title><guid>ep-1</guid>
                <enclosure url="https://cdn.example.com/a.mp3"/></item>
            </channel></rss>
            """);

        Assert.Equal("https://cdn.example.com/legacy.png", Assert.Single(feed.Episodes).ImageUrl);
    }

    [Fact]
    public async Task Fetch_NoNotesOrArtwork_LeavesBothNull()
    {
        var feed = await FetchAsync("""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>S</title>
              <item><title>A</title><guid>ep-1</guid>
                <enclosure url="https://cdn.example.com/a.mp3"/></item>
            </channel></rss>
            """);

        var episode = Assert.Single(feed.Episodes);
        Assert.Null(episode.Description);
        Assert.Null(episode.ImageUrl);
    }
}
