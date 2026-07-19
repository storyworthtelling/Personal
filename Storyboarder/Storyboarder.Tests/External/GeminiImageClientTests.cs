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

        await client.GeneratePanel("knight fights dragon", "noir", [refA, refB], aspectRatio: null, CancellationToken.None);

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
    public async Task ResourceExhausted_ThrowsTransientRateLimit()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}");

        var ex = await Assert.ThrowsAsync<RateLimitException>(() =>
            client.GenerateReferenceSheet("x", "noir", CancellationToken.None));

        // A bare per-minute throttle (no billing/credit wording) clears on its own — treat it as transient.
        Assert.Equal(RateLimitKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task DepletedCredits_ThrowsQuotaExhaustedRateLimit()
    {
        var (client, handler) = Create();
        // The real 429 body Gemini returns once an API project's prepaid credits run out.
        handler.Enqueue(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"code\":429,\"message\":\"Your prepayment credits are depleted. Please go to " +
            "AI Studio to manage your project and billing.\",\"status\":\"RESOURCE_EXHAUSTED\"}}");

        var ex = await Assert.ThrowsAsync<RateLimitException>(() =>
            client.GenerateReferenceSheet("x", "noir", CancellationToken.None));

        // Depleted billing is a quota wall, not a short throttle — flagged so the worker defers it long
        // and reports an actionable message rather than retrying every few seconds.
        Assert.Equal(RateLimitKind.QuotaExhausted, ex.Kind);
        Assert.Contains("quota exhausted", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task SafetyBlock_ThrowsAdjustableBlock_WithFlaggedCategory()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(
            "{\"candidates\":[{\"finishReason\":\"SAFETY\",\"safetyRatings\":[" +
            "{\"category\":\"HARM_CATEGORY_SEXUALLY_EXPLICIT\",\"probability\":\"NEGLIGIBLE\"}," +
            "{\"category\":\"HARM_CATEGORY_DANGEROUS_CONTENT\",\"probability\":\"HIGH\",\"blocked\":true}]}]}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GeneratePanel("a brutal duel", "noir", [], aspectRatio: null, CancellationToken.None));

        Assert.Equal(ImageBlockReason.Safety, ex.Reason);
        // Only the category that actually tripped is carried, so only it gets relaxed on re-render.
        Assert.Equal(new[] { "HARM_CATEGORY_DANGEROUS_CONTENT" }, ex.Categories);
    }

    [Fact]
    public async Task ImageSafetyFinishReason_ThrowsFilteredBlock()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"candidates\":[{\"finishReason\":\"IMAGE_SAFETY\"}]}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GeneratePanel("a brutal beat", "noir", [], aspectRatio: null, CancellationToken.None));

        // IMAGE_SAFETY is Gemini's non-configurable image-output filter, not an IP/recitation block — it's
        // recoverable by rephrasing, so it's Filtered (degrade the panel) rather than Prohibited (cancel).
        Assert.Equal(ImageBlockReason.Filtered, ex.Reason);
        Assert.Empty(ex.Categories);
    }

    [Fact]
    public async Task ProhibitedFinishReason_ThrowsProhibitedBlock()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"candidates\":[{\"finishReason\":\"PROHIBITED_CONTENT\"}]}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GeneratePanel("a recognizable trademarked hero", "noir", [], aspectRatio: null, CancellationToken.None));

        Assert.Equal(ImageBlockReason.Prohibited, ex.Reason);
        Assert.Empty(ex.Categories);
    }

    [Fact]
    public async Task PromptBlockReason_ThrowsProhibitedBlock()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"promptFeedback\":{\"blockReason\":\"BLOCKLIST\"}}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GeneratePanel("x", "noir", [], aspectRatio: null, CancellationToken.None));

        Assert.Equal(ImageBlockReason.Prohibited, ex.Reason);
    }

    [Fact]
    public async Task RelaxedRender_EmitsSafetySettings_ForOnlyTheGivenCategories()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        var image = await client.GeneratePanel(
            "a brutal duel", "noir", [], aspectRatio: null, ["HARM_CATEGORY_DANGEROUS_CONTENT"], CancellationToken.None);

        Assert.Equal(PngBytes, image);
        var body = handler.RequestBodies[0];
        Assert.Contains("safetySettings", body);
        Assert.Contains("HARM_CATEGORY_DANGEROUS_CONTENT", body);
        Assert.Contains("BLOCK_NONE", body);
        // The category that did not trip is not mentioned, so its default threshold is untouched.
        Assert.DoesNotContain("HARM_CATEGORY_SEXUALLY_EXPLICIT", body);
    }

    [Fact]
    public async Task DefaultRender_OmitsSafetySettingsEntirely()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GeneratePanel("a quiet scene", "noir", [], aspectRatio: null, CancellationToken.None);

        // The normal path never lowers thresholds — no safetySettings block is sent at all.
        Assert.DoesNotContain("safetySettings", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Panel_WithAspectRatio_RequestsNearestSupportedRatio()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        // 1.3 is closest to 4:3 (1.333) among the supported ratios.
        await client.GeneratePanel("a wide vista", "noir", [], aspectRatio: 1.3, CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("imageConfig", body);
        Assert.Contains("\"aspectRatio\":\"4:3\"", body);
    }

    [Fact]
    public async Task Panel_WithoutAspectRatio_OmitsImageConfig()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GeneratePanel("a scene", "noir", [], aspectRatio: null, CancellationToken.None);

        Assert.DoesNotContain("imageConfig", handler.RequestBodies[0]);
    }
}
