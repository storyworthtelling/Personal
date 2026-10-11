using System.Net;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

public class StreamingAvailabilityClientTests
{
    private readonly FakeHttpMessageHandler handler = new();
    private readonly FakeKeys keys = new();
    private readonly LookupTally tally = new();
    private readonly RecordingHealthSink health = new();

    private StreamingAvailabilityClient Client(StreamingAvailabilityRoute route = StreamingAvailabilityRoute.Direct) =>
        new(new HttpClient(handler), keys, ServiceCatalogue.Public, tally, health, route);

    [Fact]
    public async Task AShowIsAskedForByIdWithTheKeyInTheHeaderAndEpisodesIncluded()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Series);

        await Client().GetTitleAsync("70001", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.movieofthenight.com", request.Uri.Host);
        Assert.Equal("/v4/shows/70001", request.Uri.AbsolutePath);
        Assert.Equal("us", request.Query("country"));
        Assert.Equal("episode", request.Query("series_granularity"));
        Assert.Equal("invented-sa-key", request.Headers["X-API-Key"]);
        Assert.DoesNotContain("invented-sa-key", request.Uri.ToString());
        Assert.Equal(1, tally.Count(LookupSource.StreamingAvailability));
        Assert.Equal([PulpCredentialIds.StreamingAvailability], health.Ok);
    }

    [Fact]
    public async Task AShowIsMappedIntoTheCatalogue()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Series);

        var show = await Client().GetTitleAsync("70001", CancellationToken.None);

        Assert.NotNull(show);
        Assert.Equal(new TitleId("70001"), show.Id);
        Assert.Equal(TitleKind.Show, show.Kind);
        Assert.Equal("The Lantern Keepers", show.Name);
        Assert.Equal("tt9900001", show.ImdbId);
        Assert.Equal(2024, show.Year);
        Assert.Equal(["Drama", "Mystery"], show.Genres);
        Assert.StartsWith("Two keepers", show.Synopsis);
        Assert.Equal("https://example.com/poster/lantern-480.jpg", show.PosterUrl);
        Assert.Null(show.RuntimeMinutes);
        Assert.Equal(2, show.SeasonCount);
        Assert.Equal(3, show.EpisodeCount);
        Assert.False(show.Rating.IsRated);

        var netflix = show.Offers[0];
        Assert.Equal(new ServiceId("netflix"), netflix.Service);
        Assert.True(netflix.KnownService);
        Assert.Equal(OfferKind.Subscription, netflix.Kind);
        Assert.Equal(VideoQuality.Uhd, netflix.Quality);
        Assert.Equal("https://example.com/n/title/70001", netflix.Link);
        Assert.Equal("https://example.com/n/watch/70001", netflix.VideoLink);
        Assert.Equal(new DateOnly(2026, 11, 1), netflix.ExpiresOn);
        Assert.True(netflix.ExpiresSoon);

        var buy = show.Offers[1];
        Assert.Equal(new ServiceId("corner"), buy.Service);
        Assert.False(buy.KnownService);
        Assert.Equal("Corner Video Store", buy.ServiceName);
        Assert.Equal(OfferKind.Buy, buy.Kind);
        Assert.Equal(24.99m, buy.Price);
        Assert.Equal("USD", buy.Currency);
        Assert.Null(buy.ExpiresOn);
    }

    [Fact]
    public async Task SeasonsAndEpisodesAreNumberedByTheirPlaceInTheList()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Series);

        var show = await Client().GetTitleAsync("70001", CancellationToken.None);

        Assert.NotNull(show);
        Assert.Equal([1, 2], show.Seasons.Select(season => season.Number));
        Assert.Equal("Season 1", show.Seasons[0].Name);
        Assert.Equal([new EpisodeRef(1, 1), new EpisodeRef(1, 2)], show.Seasons[0].Episodes.Select(e => e.Episode));
        var first = show.Seasons[0].Episodes[0];
        Assert.Equal("Lamplight", first.Name);
        Assert.Equal("The first letter arrives soaked.", first.Synopsis);
        Assert.Equal(2024, first.AirYear);
        Assert.Null(first.AirDate);
        // A quad-HD picture is not reported as ultra-HD: it reads as HD rather than overstate.
        Assert.Equal(VideoQuality.Hd, Assert.Single(first.Offers).Quality);
        Assert.Equal(new EpisodeRef(2, 1), Assert.Single(show.Seasons[1].Episodes).Episode);
    }

    [Fact]
    public async Task AFilmsAddonFreeAndRentOffersAreKeptApart()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Film);

        var film = await Client().GetTitleAsync("70002", CancellationToken.None);

        Assert.NotNull(film);
        Assert.Equal(TitleKind.Film, film.Kind);
        Assert.Equal(2023, film.Year);
        Assert.Equal(104, film.RuntimeMinutes);
        Assert.Null(film.SeasonCount);
        Assert.Empty(film.Seasons);
        var addon = film.Offers.Single(offer => offer.Kind == OfferKind.Addon);
        Assert.Equal(new ServiceId("prime-video"), addon.Service);
        Assert.True(addon.KnownService);
        Assert.Equal([new StreamOffer(new ServiceId("driftwood"), VideoQuality.Sd, AlwaysWithAds: true)], TitleOffers.Streams(film.Offers));
        Assert.Equal(3.99m, Assert.Single(TitleOffers.Paid(film.Offers)).Price);
    }

    [Fact]
    public async Task TheRapidApiRouteUsesItsOwnHostAndKeyHeader()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Film);

        await Client(StreamingAvailabilityRoute.RapidApi).GetTitleAsync("70002", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("streaming-availability.p.rapidapi.com", request.Uri.Host);
        Assert.Equal("/shows/70002", request.Uri.AbsolutePath);
        Assert.Equal("invented-sa-key", request.Headers["X-RapidAPI-Key"]);
        Assert.False(request.Headers.ContainsKey("X-API-Key"));
    }

    [Fact]
    public async Task AnUnknownTitleIsNullNotAFailure()
    {
        handler.Enqueue(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");

        var title = await Client().GetTitleAsync("70099", CancellationToken.None);

        Assert.Null(title);
        Assert.Empty(health.Failing);
    }

    [Fact]
    public async Task ARejectedKeyIsReportedAndThrownWithoutTheKeyInTheMessage()
    {
        handler.Enqueue(HttpStatusCode.Unauthorized, """{ "message": "Invalid API key" }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetTitleAsync("70001", CancellationToken.None));

        Assert.Equal(LookupFailure.Rejected, thrown.Failure);
        Assert.Equal(LookupSource.StreamingAvailability, thrown.Source);
        Assert.Equal(401, thrown.StatusCode);
        Assert.DoesNotContain("invented-sa-key", thrown.Message);
        Assert.Equal([PulpCredentialIds.StreamingAvailability], health.Failing);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, LookupFailure.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, LookupFailure.Unavailable)]
    public async Task ThrottlingAndServerTroubleAreNotTheKeysFault(HttpStatusCode status, LookupFailure expected)
    {
        handler.Enqueue(status, """{ "message": "Something" }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetTitleAsync("70001", CancellationToken.None));

        Assert.Equal(expected, thrown.Failure);
        Assert.Empty(health.Failing);
    }

    [Fact]
    public async Task AnUnreadableBodyIsMalformed()
    {
        handler.EnqueueOk("<html>not the api</html>");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetTitleAsync("70001", CancellationToken.None));

        Assert.Equal(LookupFailure.Malformed, thrown.Failure);
    }

    [Fact]
    public async Task NoKeyMeansNoRequestAndNoCount()
    {
        keys.Keys = keys.Keys with { StreamingAvailabilityKey = "" };

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetTitleAsync("70001", CancellationToken.None));

        Assert.Equal(LookupFailure.MissingKey, thrown.Failure);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, tally.Total);
    }

    [Fact]
    public async Task TheKeyIsReadLiveOnEveryCall()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Film).EnqueueOk(StreamingAvailabilityFixtures.Film);
        var client = Client();

        await client.GetTitleAsync("70002", CancellationToken.None);
        keys.Keys = keys.Keys with { StreamingAvailabilityKey = "invented-restored-key" };
        await client.GetTitleAsync("70002", CancellationToken.None);

        Assert.Equal("invented-restored-key", handler.Requests[1].Headers["X-API-Key"]);
    }

    [Fact]
    public async Task SearchByTitleAsksWithoutEpisodesAndNarrowsByKindWhenGiven()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.SearchResults).EnqueueOk("[]");
        var client = Client();

        var found = await client.SearchByTitleAsync("Lantern", null, CancellationToken.None);
        await client.SearchByTitleAsync("Lantern", TitleKind.Show, CancellationToken.None);

        Assert.Equal(["Harbour of Small Hours", "The Lantern Keepers"], found.Select(title => title.Name));
        var first = handler.Requests[0];
        Assert.Equal("/v4/shows/search/title", first.Uri.AbsolutePath);
        Assert.Equal("Lantern", first.Query("title"));
        Assert.Equal("us", first.Query("country"));
        Assert.Equal("show", first.Query("series_granularity"));
        Assert.Null(first.Query("show_type"));
        Assert.Equal("series", handler.Requests[1].Query("show_type"));
    }

    [Fact]
    public async Task ChangesAreAskedForTheHouseholdsServicesBySourceId()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Changes);
        var household = new[] { ServiceCatalogue.Public.ById(new ServiceId("netflix"))!, ServiceCatalogue.Public.ById(new ServiceId("hbo-max"))! };

        await Client().GetChangesAsync(household, ChangeKind.Expiring, ChangeItem.Title, "70000:before", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v4/changes", request.Uri.AbsolutePath);
        Assert.Equal("netflix,hbo", request.Query("catalogs"));
        Assert.Equal("expiring", request.Query("change_type"));
        Assert.Equal("show", request.Query("item_type"));
        Assert.Equal("true", request.Query("include_unknown_dates"));
        Assert.Equal("70000:before", request.Query("cursor"));
        Assert.Equal("us", request.Query("country"));
    }

    [Fact]
    public async Task ChangesAreMappedWithTheirTitlesAndTheNextCursor()
    {
        handler.EnqueueOk(StreamingAvailabilityFixtures.Changes);
        var household = new[] { ServiceCatalogue.Public.ById(new ServiceId("hulu"))! };

        var page = await Client().GetChangesAsync(household, ChangeKind.Expiring, ChangeItem.Episode, null, CancellationToken.None);

        Assert.Null(handler.Requests[0].Query("cursor"));
        Assert.Equal("episode", handler.Requests[0].Query("item_type"));
        Assert.Equal(2, page.Changes.Count);
        var film = page.Changes[0];
        Assert.Equal(ChangeKind.Expiring, film.Kind);
        Assert.Equal(ChangeItem.Title, film.Item);
        Assert.Equal(new TitleId("70002"), film.Title);
        Assert.Equal(TitleKind.Film, film.TitleKind);
        Assert.Equal(new ServiceId("hulu"), film.Service);
        Assert.Equal(OfferKind.Subscription, film.Offer);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), film.At);
        Assert.Equal("https://example.com/h/70002", film.Link);
        var episode = page.Changes[1];
        Assert.Equal(ChangeItem.Episode, episode.Item);
        Assert.Equal(1, episode.Season);
        Assert.Equal(2, episode.Episode);
        Assert.Null(episode.At);
        Assert.Null(episode.Link);
        Assert.Equal("Harbour of Small Hours", page.Titles[new TitleId("70002")].Name);
        Assert.Equal(2, page.Titles.Count);
        Assert.Equal("70001:invented", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ChangesForNoServicesAreRefusedBeforeAnyRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Client().GetChangesAsync([], ChangeKind.New, ChangeItem.Title, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }
}
