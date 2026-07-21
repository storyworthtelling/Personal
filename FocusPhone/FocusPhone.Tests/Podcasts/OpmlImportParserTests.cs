using FocusPhone.Infrastructure.Podcasts;
using Xunit;

namespace FocusPhone.Tests.Podcasts;

public class OpmlImportParserTests
{
    [Fact]
    public void Parse_ReadsFeedsWithTitles()
    {
        const string opml = """
            <opml version="2.0">
              <body>
                <outline type="rss" text="Show A" xmlUrl="https://a.com/feed" />
                <outline type="rss" text="Show B" xmlUrl="https://b.com/feed" />
              </body>
            </opml>
            """;

        var subscriptions = OpmlImportParser.Parse(opml);

        Assert.Equal(
            [("Show A", "https://a.com/feed"), ("Show B", "https://b.com/feed")],
            subscriptions.Select(subscription => (subscription.Title, subscription.FeedUrl)).ToArray());
    }

    [Fact]
    public void Parse_FindsFeedsNestedUnderCategories()
    {
        const string opml = """
            <opml version="2.0">
              <body>
                <outline text="News">
                  <outline type="rss" text="Daily" xmlUrl="https://news.com/feed" />
                </outline>
              </body>
            </opml>
            """;

        var subscription = Assert.Single(OpmlImportParser.Parse(opml));
        Assert.Equal("https://news.com/feed", subscription.FeedUrl);
    }

    [Fact]
    public void Parse_SkipsCategoryOutlinesWithoutFeeds()
    {
        const string opml = """
            <opml version="2.0">
              <body>
                <outline text="Empty Folder" />
                <outline type="rss" text="Real" xmlUrl="https://real.com/feed" />
              </body>
            </opml>
            """;

        Assert.Single(OpmlImportParser.Parse(opml));
    }

    [Fact]
    public void Parse_DedupesByFeedUrl()
    {
        const string opml = """
            <opml version="2.0">
              <body>
                <outline type="rss" text="Show A" xmlUrl="https://a.com/feed" />
                <outline type="rss" text="Show A copy" xmlUrl="https://a.com/feed" />
              </body>
            </opml>
            """;

        Assert.Single(OpmlImportParser.Parse(opml));
    }

    [Fact]
    public void Parse_FallsBackToFeedUrlWhenTitleMissing()
    {
        const string opml = """
            <opml version="2.0"><body>
              <outline type="rss" xmlUrl="https://a.com/feed" />
            </body></opml>
            """;

        var subscription = Assert.Single(OpmlImportParser.Parse(opml));
        Assert.Equal("https://a.com/feed", subscription.Title);
    }
}
