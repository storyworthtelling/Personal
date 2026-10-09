using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Ratings;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// OMDb, shapes from https://www.omdbapi.com/ (the key is the <c>apikey</c> query parameter; a title
/// lookup is <c>i=</c> an IMDb id; the answer's <c>Rated</c> is the code). Invented titles.
/// </summary>
public class OmdbClientTests
{
    private const string Found = """
    { "Title": "Harbour of Small Hours", "Year": "2023", "Rated": "PG-13", "Released": "01 Mar 2023",
      "Plot": "An invented plot that Pulp never reads.", "imdbID": "tt9900002", "Type": "movie", "Response": "True" }
    """;

    private readonly FakeHttpMessageHandler handler = new();
    private readonly FakeKeys keys = new();
    private readonly LookupTally tally = new();
    private readonly RecordingHealthSink health = new();

    private OmdbClient Client() => new(new HttpClient(handler), keys, tally, health);

    [Fact]
    public async Task TheRatingCodeIsReadByImdbIdWithTheKeyAsAQueryParameter()
    {
        handler.EnqueueOk(Found);

        var rating = await Client().GetRatingAsync("tt9900002", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("www.omdbapi.com", request.Uri.Host);
        Assert.Equal("tt9900002", request.Query("i"));
        Assert.Equal("invented-omdb-key", request.Query("apikey"));
        Assert.Equal(ContentRating.Parse("PG-13"), rating);
        Assert.Equal(RatingStep.Teen, rating.Step);
        Assert.Equal(1, tally.Count(LookupSource.Omdb));
        Assert.Equal([PulpCredentialIds.Omdb], health.Ok);
    }

    [Theory]
    [InlineData("""{ "Title": "Harbour of Small Hours", "Rated": "N/A", "Response": "True" }""")]
    [InlineData("""{ "Response": "False", "Error": "Incorrect IMDb ID." }""")]
    public async Task NoUsableRatingIsUnrated(string body)
    {
        handler.EnqueueOk(body);

        var rating = await Client().GetRatingAsync("tt9900002", CancellationToken.None);

        Assert.False(rating.IsRated);
        Assert.Empty(health.Failing);
    }

    [Fact]
    public async Task AnInvalidKeyIsReportedAndThrown()
    {
        handler.Enqueue(HttpStatusCode.Unauthorized, """{ "Response": "False", "Error": "Invalid API key!" }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetRatingAsync("tt9900002", CancellationToken.None));

        Assert.Equal(LookupFailure.Rejected, thrown.Failure);
        Assert.Equal([PulpCredentialIds.Omdb], health.Failing);
        Assert.DoesNotContain("invented-omdb-key", thrown.Message);
    }

    [Fact]
    public async Task TheDailyLimitIsThrottlingNotABadKey()
    {
        handler.Enqueue(HttpStatusCode.Unauthorized, """{ "Response": "False", "Error": "Request limit reached!" }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetRatingAsync("tt9900002", CancellationToken.None));

        Assert.Equal(LookupFailure.Throttled, thrown.Failure);
        Assert.Empty(health.Failing);
    }

    [Fact]
    public async Task NoKeyMeansNoRequest()
    {
        keys.Keys = keys.Keys with { OmdbKey = " " };

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().GetRatingAsync("tt9900002", CancellationToken.None));

        Assert.Equal(LookupFailure.MissingKey, thrown.Failure);
        Assert.Empty(handler.Requests);
    }
}
