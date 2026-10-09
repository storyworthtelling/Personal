using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Library;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// TVmaze, shapes from https://www.tvmaze.com/api (show lookup answers with a 301 to the show; the
/// episode list carries season, number, airdate and airstamp). Invented shows and episodes.
/// </summary>
public class TvMazeClientTests
{
    private const string Show = """
    { "id": 44001, "url": "https://example.com/shows/44001", "name": "The Lantern Keepers", "status": "Ended",
      "externals": { "tvrage": null, "thetvdb": 990001, "imdb": "tt9900001" } }
    """;

    private const string Episodes = """
    [
      { "id": 1, "name": "Lamplight", "season": 1, "number": 1, "type": "regular", "airdate": "2024-01-02", "airtime": "21:00", "airstamp": "2024-01-03T02:00:00+00:00", "runtime": 50 },
      { "id": 2, "name": "A Special Evening", "season": 1, "number": null, "type": "significant_special", "airdate": "2024-01-05", "airtime": "", "airstamp": "2024-01-05T12:00:00+00:00", "runtime": 30 },
      { "id": 3, "name": "Tidewater", "season": 1, "number": 2, "type": "regular", "airdate": "", "airtime": "", "airstamp": null, "runtime": null }
    ]
    """;

    private readonly FakeHttpMessageHandler handler = new();
    private readonly LookupTally tally = new();

    private TvMazeClient Client() => new(new HttpClient(handler), tally);

    [Fact]
    public async Task TheShowIsLookedUpByImdbIdThenItsEpisodesAreRead()
    {
        handler.EnqueueOk(Show).EnqueueOk(Episodes);

        var dates = await Client().GetAirDatesByImdbAsync("tt9900001", CancellationToken.None);

        Assert.Equal("api.tvmaze.com", handler.Requests[0].Uri.Host);
        Assert.Equal("/lookup/shows", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("tt9900001", handler.Requests[0].Query("imdb"));
        Assert.Equal("/shows/44001/episodes", handler.Requests[1].Uri.AbsolutePath);
        // TVmaze is keyless: no request carries a key in a header or the query.
        Assert.All(handler.Requests, request => Assert.DoesNotContain(request.Headers.Keys, name => name.Contains("key", StringComparison.OrdinalIgnoreCase)));
        Assert.All(handler.Requests, request => Assert.DoesNotContain("key", request.Uri.Query, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, tally.Count(LookupSource.TvMaze));

        Assert.NotNull(dates);
        Assert.True(dates.Ended);
        // The special has no number, so it has no place in the season and is left out.
        Assert.Equal([new EpisodeRef(1, 1), new EpisodeRef(1, 2)], dates.Episodes.Select(e => e.Episode));
        Assert.Equal(new DateOnly(2024, 1, 2), dates.Episodes[0].AirDate);
        Assert.Equal(new DateTimeOffset(2024, 1, 3, 2, 0, 0, TimeSpan.Zero), dates.Episodes[0].AirsAt);
        Assert.Equal("Tidewater", dates.Episodes[1].Name);
        Assert.Null(dates.Episodes[1].AirDate);
        Assert.Null(dates.Episodes[1].AirsAt);
    }

    [Fact]
    public async Task ARedirectThatWasNotFollowedIsFollowedByHand()
    {
        handler.Enqueue(HttpStatusCode.MovedPermanently, string.Empty, "https://api.tvmaze.com/shows/44001")
            .EnqueueOk(Show.Replace("Ended", "Running"))
            .EnqueueOk(Episodes);

        var dates = await Client().GetAirDatesByImdbAsync("tt9900001", CancellationToken.None);

        Assert.Equal("/shows/44001", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal("/shows/44001/episodes", handler.Requests[2].Uri.AbsolutePath);
        Assert.NotNull(dates);
        Assert.False(dates.Ended);
        Assert.Equal(3, tally.Count(LookupSource.TvMaze));
    }

    [Fact]
    public async Task AShowTvMazeDoesNotKnowIsNull()
    {
        handler.Enqueue(HttpStatusCode.NotFound, string.Empty);

        Assert.Null(await Client().GetAirDatesByImdbAsync("tt9900009", CancellationToken.None));
    }

    [Fact]
    public async Task ThrottlingIsThrownAsThrottled()
    {
        handler.Enqueue(HttpStatusCode.TooManyRequests, string.Empty);

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetAirDatesByImdbAsync("tt9900001", CancellationToken.None));

        Assert.Equal(LookupFailure.Throttled, thrown.Failure);
        Assert.Equal(LookupSource.TvMaze, thrown.Source);
    }
}
