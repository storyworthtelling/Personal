using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// The highlights adapter (D313): one search inside the league's official channel, around the game's day (to 14 days after, D349), for an
/// embeddable video. It returns the video id and nothing else, so a title cannot reach a screen.
/// </summary>
public class YouTubeHighlightsSourceTests
{
    private readonly FakeHttpMessageHandler handler = new();

    private IHighlightsSource Source() => new YouTubeHighlightsSource(new YouTubeClient(new HttpClient(handler), new FakeKeys()));

    private static readonly HighlightsRequest Request = new(
        new LeagueId("nfl"), "channel-invented-1", "Gulls", "Foxes", new DateTimeOffset(2026, 1, 17, 20, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task TheSearchIsInsideTheOfficialChannelAroundTheGamesDay()
    {
        handler.EnqueueOk("""{ "items": [ { "id": { "videoId": "inventVid_9" }, "snippet": { "title": "Gulls crush Foxes 42-3" } } ] }""");

        var video = await Source().FindVideoIdAsync(Request, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("channel-invented-1", sent.Query("channelId"));
        Assert.Equal("true", sent.Query("videoEmbeddable"));
        Assert.Equal("video", sent.Query("type"));
        Assert.Equal("1", sent.Query("maxResults"));
        Assert.Equal("Foxes Gulls highlights", sent.Query("q"));
        Assert.Equal("2026-01-17T00:00:00Z", sent.Query("publishedAfter"));
        Assert.Equal("2026-01-31T00:00:00Z", sent.Query("publishedBefore"));
        Assert.Equal("invented-yt-key", sent.Query("key"));
        Assert.Equal("inventVid_9", video);
    }

    [Fact]
    public async Task NoResultIsNull()
    {
        handler.EnqueueOk("""{ "items": [] }""");

        Assert.Null(await Source().FindVideoIdAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task AVideoIdThatIsNotAYouTubeIdIsTreatedAsNone()
    {
        handler.EnqueueOk("""{ "items": [ { "id": { "videoId": "x\" onload=\"bad" } } ] }""");

        Assert.Null(await Source().FindVideoIdAsync(Request, CancellationToken.None));
    }

    [Fact]
    public void TheResultTypeHasNoRoomForATitle()
    {
        var method = typeof(IHighlightsSource).GetMethod(nameof(IHighlightsSource.FindVideoIdAsync))!;

        Assert.Equal(typeof(Task<string?>), method.ReturnType);
    }

    [Fact]
    public void OnlyLeaguesWithAnOfficialChannelOnRecordHaveOne()
    {
        Assert.NotNull(HighlightChannels.Default.ChannelOf(new LeagueId("nfl")));
        Assert.Null(HighlightChannels.Default.ChannelOf(new LeagueId("afl")));
        Assert.True(HighlightChannels.Default.Has(new LeagueId("MLB")));
    }

    [Fact]
    public void TheTableHoldsSixDistinctWellFormedChannelIds()
    {
        var leagues = new[] { "nfl", "mlb", "nba", "nhl", "mls", "nwsl" };
        var ids = leagues.Select(l => HighlightChannels.Default.ChannelOf(new LeagueId(l))).ToList();

        Assert.All(ids, id => Assert.Matches("^UC[A-Za-z0-9_-]{22}$", id!));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
