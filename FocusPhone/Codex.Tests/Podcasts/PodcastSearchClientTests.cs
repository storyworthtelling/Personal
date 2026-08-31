using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Podcasts;

public class PodcastSearchClientTests
{
    private const string Url = "https://itunes.apple.com/search?media=podcast&limit=25&term=the%20habit";

    private const string Body = """
        {"resultCount":2,"results":[
          {"collectionName":"The Habit","artistName":"Jonathan Rogers",
           "feedUrl":"https://feeds.example.com/habit","artworkUrl600":"https://img.example.com/habit.jpg"},
          {"collectionName":"Second Show","artistName":"Someone",
           "feedUrl":"https://feeds.example.com/second"}]}
        """;

    private static (PodcastSearchClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new PodcastSearchClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_ReturnsTheShowsWithTheirFeeds()
    {
        var (client, handler) = Build();
        handler.Respond(Url, Body);

        var results = await client.SearchAsync("the habit", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("The Habit", results[0].Title);
        Assert.Equal("Jonathan Rogers", results[0].Author);
        Assert.Equal("https://feeds.example.com/habit", results[0].FeedUrl);
        Assert.Equal("https://img.example.com/habit.jpg", results[0].ArtworkUrl);
    }

    [Fact]
    public async Task SearchAsync_EscapesTheQuery()
    {
        var (client, handler) = Build();
        handler.Respond(Url, Body);

        await client.SearchAsync("the habit", CancellationToken.None);

        Assert.Equal(Url, handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    // A show with no feed cannot be subscribed to, so listing it would be an offer
    // the screen could not keep.
    [Fact]
    public async Task SearchAsync_DropsResultsWithNoFeed()
    {
        var (client, handler) = Build();
        handler.Respond(Url, """{"resultCount":1,"results":[{"collectionName":"No Feed","artistName":"X"}]}""");

        Assert.Empty(await client.SearchAsync("the habit", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ReturnsNothingForABlankQuery()
    {
        var (client, handler) = Build();

        Assert.Empty(await client.SearchAsync("   ", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"results":"unexpected"}""")]
    public async Task SearchAsync_ReturnsNothingForAReplyItCannotRead(string body)
    {
        var (client, handler) = Build();
        handler.Respond(Url, body);

        Assert.Empty(await client.SearchAsync("the habit", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ReturnsNothingWhenTheServiceIsDown()
    {
        var (client, _) = Build();

        Assert.Empty(await client.SearchAsync("the habit", CancellationToken.None));
    }
}
