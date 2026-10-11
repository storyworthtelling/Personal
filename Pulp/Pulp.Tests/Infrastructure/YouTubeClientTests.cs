using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// YouTube Data API search.list, shapes from https://developers.google.com/youtube/v3/docs/search/list
/// (the key is the <c>key</c> query parameter; a result's video id is <c>items[].id.videoId</c>).
/// </summary>
public class YouTubeClientTests
{
    private const string Results = """
    { "kind": "youtube#searchListResponse", "items": [
      { "kind": "youtube#searchResult", "id": { "kind": "youtube#video", "videoId": "inventedVid1" },
        "snippet": { "title": "Invented trailer" } } ] }
    """;

    private readonly FakeHttpMessageHandler handler = new();
    private readonly FakeKeys keys = new();
    private readonly LookupTally tally = new();
    private readonly RecordingHealthSink health = new();

    private YouTubeClient Client() => new(new HttpClient(handler), keys, tally, health);

    [Fact]
    public async Task ATrailerIsSearchedForOneEmbeddableVideo()
    {
        handler.EnqueueOk(Results);

        var video = await Client().FindTrailerAsync("The Lantern Keepers", 2024, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("www.googleapis.com", request.Uri.Host);
        Assert.Equal("/youtube/v3/search", request.Uri.AbsolutePath);
        Assert.Equal("snippet", request.Query("part"));
        Assert.Equal("video", request.Query("type"));
        Assert.Equal("1", request.Query("maxResults"));
        Assert.Equal("true", request.Query("videoEmbeddable"));
        Assert.Equal("The Lantern Keepers 2024 official trailer", request.Query("q"));
        Assert.Equal("invented-yt-key", request.Query("key"));
        Assert.Equal("inventedVid1", video);
        Assert.Equal(1, tally.Count(LookupSource.YouTube));
        Assert.Equal([PulpCredentialIds.YouTube], health.Ok);
    }

    [Fact]
    public async Task NoResultIsNull()
    {
        handler.EnqueueOk("""{ "kind": "youtube#searchListResponse", "items": [] }""");

        Assert.Null(await Client().FindTrailerAsync("Harbour of Small Hours", null, CancellationToken.None));
        Assert.Equal("Harbour of Small Hours official trailer", handler.Requests[0].Query("q"));
    }

    [Fact]
    public async Task AnInvalidKeyIsReportedAndThrown()
    {
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": { "code": 400, "message": "API key not valid. Please pass a valid API key.", "status": "INVALID_ARGUMENT", "details": [ { "reason": "API_KEY_INVALID" } ] } }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().FindTrailerAsync("The Lantern Keepers", 2024, CancellationToken.None));

        Assert.Equal(LookupFailure.Rejected, thrown.Failure);
        Assert.Equal([PulpCredentialIds.YouTube], health.Failing);
        Assert.DoesNotContain("invented-yt-key", thrown.Message);
    }

    [Fact]
    public async Task AnExhaustedQuotaIsThrottlingNotABadKey()
    {
        handler.Enqueue(HttpStatusCode.Forbidden, """{ "error": { "code": 403, "message": "The request cannot be completed because you have exceeded your quota.", "errors": [ { "reason": "quotaExceeded", "domain": "youtube.quota" } ] } }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Client().FindTrailerAsync("The Lantern Keepers", 2024, CancellationToken.None));

        Assert.Equal(LookupFailure.Throttled, thrown.Failure);
        Assert.Empty(health.Failing);
    }
}
