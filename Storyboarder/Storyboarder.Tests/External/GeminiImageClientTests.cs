using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.External;

public class GeminiImageClientTests
{
    private static readonly byte[] PngBytes = [137, 80, 78, 71, 1, 2, 3, 4];

    private static (GeminiImageClient Client, FakeHttpMessageHandler Handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new GeminiOptions { ApiKey = "secret key", Model = "gemini-2.5-flash-image" });
        return (new GeminiImageClient(new HttpClient(handler), options), handler);
    }

    private static string ImageReply(byte[] bytes) =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(bytes) } } } } }
            }
        });

    private static int Count(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task GenerateReferenceSheet_ReturnsDecodedImage_AndSendsTextOnly()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        var image = await client.GenerateReferenceSheet("a tall knight", "noir", CancellationToken.None);

        Assert.Equal(PngBytes, image);
        Assert.DoesNotContain("inlineData", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task GeneratePanel_AttachesReferenceImagesAsInlineData()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));
        byte[] refA = [1, 1, 1];
        byte[] refB = [2, 2, 2];

        await client.GeneratePanel("knight fights dragon", "noir", [refA, refB], CancellationToken.None);

        Assert.Equal(2, Count(handler.RequestBodies[0], "inlineData"));
        Assert.Contains(Convert.ToBase64String(refA), handler.RequestBodies[0]);
        Assert.Contains(Convert.ToBase64String(refB), handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Request_TargetsModelEndpoint_WithEscapedApiKey()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GenerateReferenceSheet("subject", "noir", CancellationToken.None);

        var uri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("gemini-2.5-flash-image:generateContent", uri);
        Assert.Contains("key=secret%20key", uri);
    }

    [Fact]
    public async Task ResourceExhausted_ThrowsRateLimitException()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}");

        await Assert.ThrowsAsync<RateLimitException>(() =>
            client.GenerateReferenceSheet("x", "noir", CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_ThrowsHttpRequestException()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GenerateReferenceSheet("x", "noir", CancellationToken.None));
    }

    [Fact]
    public async Task MissingImageInResponse_Throws()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"sorry\"}]}}]}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateReferenceSheet("x", "noir", CancellationToken.None));
    }
}
