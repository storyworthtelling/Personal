using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Folio.Domain.Pipeline;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Tests.Support;
using Xunit;
using Storyworthtelling.Pipeline;

namespace Folio.Tests.External;

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
    public async Task GenerateReferenceSheet_PromptRequestsAVisibleScaleReference()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GenerateReferenceSheet("a tall knight. Approximate scale: 1.9m.", "noir", CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("scale reference", body);
        Assert.Contains("human figure", body);
    }

    [Fact]
    public async Task SceneImage_AttachesReferenceImagesAsInlineData()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));
        byte[] refA = [1, 1, 1];
        byte[] refB = [2, 2, 2];

        await client.GenerateSceneImage("knight fights dragon", "noir", [refA, refB], aspectRatio: null, CancellationToken.None);

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
            client.GenerateSceneImage("a brutal duel", "noir", [], aspectRatio: null, CancellationToken.None));

        // Any block just means "retry a different moment, else leave the image unavailable" — Folio
        // never relaxes a category, so the block reason is what matters, not the flagged categories.
        Assert.Equal(ImageBlockReason.Safety, ex.Reason);
    }

    [Fact]
    public async Task ImageSafetyFinishReason_ThrowsFilteredBlock()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"candidates\":[{\"finishReason\":\"IMAGE_SAFETY\"}]}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GenerateSceneImage("a brutal beat", "noir", [], aspectRatio: null, CancellationToken.None));

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
            client.GenerateSceneImage("a recognizable trademarked hero", "noir", [], aspectRatio: null, CancellationToken.None));

        Assert.Equal(ImageBlockReason.Prohibited, ex.Reason);
        Assert.Empty(ex.Categories);
    }

    [Fact]
    public async Task PromptBlockReason_ThrowsProhibitedBlock()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"promptFeedback\":{\"blockReason\":\"BLOCKLIST\"}}");

        var ex = await Assert.ThrowsAsync<ImageContentBlockedException>(() =>
            client.GenerateSceneImage("x", "noir", [], aspectRatio: null, CancellationToken.None));

        Assert.Equal(ImageBlockReason.Prohibited, ex.Reason);
    }

    [Fact]
    public async Task Render_NeverEmitsSafetySettings()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GenerateSceneImage("a quiet scene", "noir", [], aspectRatio: null, CancellationToken.None);

        // Folio never lowers thresholds or asks the user to approve a render — no safetySettings block.
        Assert.DoesNotContain("safetySettings", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task SceneImage_WithAspectRatio_RequestsNearestSupportedRatio()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        // 1.3 is closest to 4:3 (1.333) among the supported ratios.
        await client.GenerateSceneImage("a wide vista", "noir", [], aspectRatio: 1.3, CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("imageConfig", body);
        Assert.Contains("\"aspectRatio\":\"4:3\"", body);
    }

    [Fact]
    public async Task SceneImage_WithoutAspectRatio_OmitsImageConfig()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(ImageReply(PngBytes));

        await client.GenerateSceneImage("a scene", "noir", [], aspectRatio: null, CancellationToken.None);

        Assert.DoesNotContain("imageConfig", handler.RequestBodies[0]);
    }

    // The scene pictures on Gemini's batch tier. Wire names read out of Google's own Gen AI .NET SDK
    // (Google.GenAI 2.2.0's Batches converters), since the docs host is not reachable from here.

    [Fact]
    public async Task ABatchOfScenePicturesIsSubmittedInlineWithEachScenesKey()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"name\":\"batches/abc123\",\"metadata\":{\"state\":\"BATCH_STATE_PENDING\"}}");

        var name = await client.SubmitSceneImages(
        [
            new SceneImageJob(3, "A knight at the gate.", [PngBytes], 2.0 / 3.0),
            new SceneImageJob(7, "A boat in fog.", [], null),
        ], "ink-wash", CancellationToken.None);

        Assert.Equal("batches/abc123", name);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/v1beta/models/gemini-2.5-flash-image:batchGenerateContent", request.RequestUri!.GetLeftPart(UriPartial.Path));
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var requests = body.RootElement.GetProperty("batch").GetProperty("inputConfig")
            .GetProperty("requests").GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(2, requests.Count);
        Assert.Equal("scene-3", requests[0].GetProperty("metadata").GetProperty("key").GetString());
        var first = requests[0].GetProperty("request");
        Assert.Contains("A knight at the gate.", first.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(Convert.ToBase64String(PngBytes),
            first.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("data").GetString());
        Assert.Equal("IMAGE", first.GetProperty("generationConfig").GetProperty("responseModalities")[0].GetString());
        Assert.Equal("2:3", first.GetProperty("generationConfig").GetProperty("imageConfig").GetProperty("aspectRatio").GetString());
        Assert.False(requests[1].GetProperty("request").GetProperty("generationConfig").TryGetProperty("imageConfig", out _));
    }

    [Fact]
    public async Task ARunningBatchHasNotEnded()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"name\":\"batches/abc123\",\"metadata\":{\"state\":\"BATCH_STATE_RUNNING\"}}");

        var poll = await client.PollSceneImages("batches/abc123", CancellationToken.None);

        Assert.False(poll.Ended);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("/v1beta/batches/abc123", handler.Requests[0].RequestUri!.GetLeftPart(UriPartial.Path));
    }

    /// <summary>Each picture is matched by its key; a refusal or an error is that scene's failure, not the batch's.</summary>
    [Fact]
    public async Task AFinishedBatchHandsBackEachScenesPictureOrWhyNot()
    {
        var (client, handler) = Create();
        var ok = ImageReply(PngBytes);
        handler.EnqueueOk(
            "{\"name\":\"batches/abc123\",\"metadata\":{\"state\":\"BATCH_STATE_SUCCEEDED\",\"output\":{\"inlinedResponses\":{\"inlinedResponses\":["
            + "{\"response\":" + ok + ",\"metadata\":{\"key\":\"scene-3\"}},"
            + "{\"response\":{\"candidates\":[{\"finishReason\":\"IMAGE_SAFETY\"}]},\"metadata\":{\"key\":\"scene-7\"}},"
            + "{\"error\":{\"code\":500,\"message\":\"internal\"},\"metadata\":{\"key\":\"scene-9\"}}"
            + "]}}}}");

        var poll = await client.PollSceneImages("batches/abc123", CancellationToken.None);

        Assert.True(poll.Ended);
        Assert.Equal(PngBytes, poll.Images.Single(item => item.SceneNumber == 3).Value);
        Assert.Null(poll.Images.Single(item => item.SceneNumber == 7).Value);
        Assert.NotNull(poll.Images.Single(item => item.SceneNumber == 7).Failure);
        Assert.Contains("internal", poll.Images.Single(item => item.SceneNumber == 9).Failure);
    }

    [Theory]
    [InlineData("BATCH_STATE_FAILED")]
    [InlineData("BATCH_STATE_EXPIRED")]
    [InlineData("BATCH_STATE_CANCELLED")]
    public async Task AFailedOrExpiredBatchHasEndedWithNoPictures(string state)
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"name\":\"batches/abc123\",\"metadata\":{\"state\":\"" + state + "\"}}");

        var poll = await client.PollSceneImages("batches/abc123", CancellationToken.None);

        Assert.True(poll.Ended);
        Assert.Empty(poll.Images);
    }
}
