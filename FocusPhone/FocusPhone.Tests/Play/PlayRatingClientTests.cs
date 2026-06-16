using FocusPhone.Domain.Apps;
using FocusPhone.Infrastructure.Play;
using Xunit;

namespace FocusPhone.Tests.Play;

public class PlayRatingClientTests
{
    private static string DetailsUrl(string packageName) =>
        $"https://play.google.com/store/apps/details?id={packageName}&hl=en";

    private static string PageWithRating(string contentRating) => $$"""
        <html><head>
        <script type="application/ld+json">
        {"@type":"SoftwareApplication","name":"Some App","contentRating":"{{contentRating}}"}
        </script>
        </head><body>store page</body></html>
        """;

    private static async Task<ContentRating> RatingForPageAsync(string body)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(DetailsUrl("com.example.app"), body, "text/html");
        var client = new PlayRatingClient(new HttpClient(handler));
        return await client.GetContentRatingAsync("com.example.app");
    }

    [Theory]
    [InlineData("Everyone", ContentRating.Everyone)]
    [InlineData("Everyone 10+", ContentRating.Everyone)]
    [InlineData("Teen", ContentRating.Teen)]
    [InlineData("Mature 17+", ContentRating.Mature)]
    [InlineData("Adults only 18+", ContentRating.Mature)]
    public async Task GetContentRating_MapsKnownRatings(string label, ContentRating expected)
    {
        Assert.Equal(expected, await RatingForPageAsync(PageWithRating(label)));
    }

    [Fact]
    public async Task GetContentRating_UnrecognizedLabel_IsUnknown()
    {
        Assert.Equal(ContentRating.Unknown, await RatingForPageAsync(PageWithRating("Rated R")));
    }

    [Fact]
    public async Task GetContentRating_PageWithoutJsonLd_IsUnknown()
    {
        Assert.Equal(ContentRating.Unknown, await RatingForPageAsync("<html><body>no metadata</body></html>"));
    }

    [Fact]
    public async Task GetContentRating_HttpFailure_IsUnknown()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new PlayRatingClient(new HttpClient(handler));

        Assert.Equal(ContentRating.Unknown, await client.GetContentRatingAsync("com.example.unlisted"));
    }
}
