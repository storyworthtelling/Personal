using System.Net;
using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class PlayListingClientTests
{
    private const string Url = "https://play.google.com/store/apps/details?id=com.example.forum&hl=en_US&gl=US";

    [Fact]
    public async Task AListingWithARating_IsRated()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, """<span itemprop="contentRating"><span>Mature 17+</span></span>""", "text/html");

        var lookup = await new PlayListingClient(new HttpClient(handler)).LookupAsync("com.example.forum");

        Assert.Equal(new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Mature), lookup);
    }

    [Fact]
    public async Task ItAsksForTheUsListing()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, """<span itemprop="contentRating"><span>Teen</span></span>""", "text/html");

        await new PlayListingClient(new HttpClient(handler)).LookupAsync("com.example.forum");

        Assert.Equal(Url, handler.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.Contains("en-US", handler.Requests.Single().Headers.AcceptLanguage.ToString());
    }

    [Fact]
    public async Task NoListing_IsNotListed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, "Not found", "text/html", HttpStatusCode.NotFound);

        var lookup = await new PlayListingClient(new HttpClient(handler)).LookupAsync("com.example.forum");

        Assert.Equal(RatingLookupOutcome.NotListed, lookup.Outcome);
    }

    [Fact]
    public async Task APageWithNoRating_IsUnreadable()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, "<html><body>A redesigned page</body></html>", "text/html");

        var lookup = await new PlayListingClient(new HttpClient(handler)).LookupAsync("com.example.forum");

        Assert.Equal(RatingLookupOutcome.Unreadable, lookup.Outcome);
    }

    [Fact]
    public async Task AServerError_IsFailed()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, "busy", "text/html", HttpStatusCode.ServiceUnavailable);

        var lookup = await new PlayListingClient(new HttpClient(handler)).LookupAsync("com.example.forum");

        Assert.Equal(RatingLookupOutcome.Failed, lookup.Outcome);
    }
}
