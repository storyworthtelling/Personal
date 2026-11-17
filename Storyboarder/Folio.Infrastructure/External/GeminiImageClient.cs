using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Folio.Domain.External;
using Folio.Domain.Health;
using Folio.Domain.Pipeline;
using Folio.Infrastructure.Configuration;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Pipeline;

namespace Folio.Infrastructure.External;

// Image generation via the Gemini (Generative Language) API. Uses an image-capable model that accepts
// reference images, so character/setting reference sheets can be supplied on every scene render to keep
// appearance consistent — coherence is part of the fidelity check. RESOURCE_EXHAUSTED / 429 surface as
// RateLimitException; a content refusal surfaces as ImageContentBlockedException so the worker can retry
// a different moment of the scene, then leave the image unavailable rather than blocking the report.
public sealed class GeminiImageClient : IGeminiImageClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient httpClient;
    private readonly GeminiOptions options;
    private readonly ICredentialHealthSink? healthSink;

    public GeminiImageClient(
        HttpClient httpClient, IOptions<GeminiOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.healthSink = healthSink;
    }

    public Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken)
    {
        var prompt =
            $"Character/location reference sheet in this style: {artStyle}. " +
            $"Render a clear, well-lit, full reference view (neutral background) of: {description}. " +
            "Include a visible scale reference so the subject's size reads correctly: for a figure, a " +
            "subtle height scale of evenly-spaced tick marks behind them; for a place, an average-height " +
            "human figure standing in it for scale. " +
            "Single subject, consistent canonical appearance, no text or lettering.";
        return Generate(prompt, referenceImages: [], aspectRatio: null, cancellationToken);
    }

    /// <summary>
    /// The book's jacket.
    ///
    /// <para><b>Wordless, deliberately.</b> Image models letter badly — a cover that says
    /// "The Salt Line" with one letter wrong is worse than one that says nothing, and it is the one
    /// mistake nobody notices until the book has been on the shelf for a week. The title is set in
    /// type over the art by the app, where it is spelt from the manuscript.</para>
    ///
    /// <para>Portrait, because a book is: 2:3 is the shape of the slot on the desk and the shelf.</para>
    /// </summary>
    public Task<byte[]> GenerateJacket(
        string title, string genre, string logline, string artStyle, CancellationToken cancellationToken)
    {
        var subject = string.IsNullOrWhiteSpace(logline) ? title : logline;
        var kind = string.IsNullOrWhiteSpace(genre) ? "literary novel" : genre;

        var prompt =
            $"Book jacket illustration for a {kind}, in this style: {artStyle}. " +
            $"The book is about: {subject}. " +
            "Compose it as cover art: one strong image that reads at a glance and at thumbnail size, " +
            "with a calm area of low detail in the lower third where a title will be set over it. " +
            "Absolutely no text, lettering, words, letters or numbers anywhere in the image.";

        return Generate(prompt, referenceImages: [], aspectRatio: 2.0 / 3.0, cancellationToken);
    }

    public Task<byte[]> GenerateSceneImage(
        string visualPrompt, string artStyle, IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio, CancellationToken cancellationToken)
    {
        var prompt = BuildSceneImagePrompt(artStyle, visualPrompt, referenceImages.Count > 0);
        return Generate(prompt, referenceImages, aspectRatio, cancellationToken);
    }

    // The wordless evidence image for a scene: one illustration of the beat, in the book's derived style,
    // with no lettering (it is the moment the prose is measured against, not a comic panel).
    private static string BuildSceneImagePrompt(string artStyle, string visualPrompt, bool hasReferences)
    {
        var referenceClause = hasReferences
            ? "Match the supplied reference images for any recurring characters or settings so they stay " +
              "recognizable across scenes. "
            : string.Empty;
        return
            $"A single wordless illustration in this style: {artStyle}. " +
            $"Depict this moment: {visualPrompt}. " +
            referenceClause +
            "Full color, portrait orientation. Absolutely no text, letters, words, captions, speech " +
            "balloons, or signage anywhere in the image.";
    }

    // The aspect ratios gemini-2.5-flash-image accepts; the requested slot aspect is snapped to the
    // nearest so the request never carries an unsupported value.
    private static readonly (string Label, double Ratio)[] SupportedAspectRatios =
    {
        ("21:9", 21.0 / 9), ("16:9", 16.0 / 9), ("3:2", 3.0 / 2), ("4:3", 4.0 / 3), ("5:4", 5.0 / 4),
        ("1:1", 1.0), ("4:5", 4.0 / 5), ("3:4", 3.0 / 4), ("2:3", 2.0 / 3), ("9:16", 9.0 / 16),
    };

    private static string NearestAspectRatio(double ratio) =>
        SupportedAspectRatios.MinBy(candidate => Math.Abs(candidate.Ratio - ratio)).Label;

    private const string ApiRoot = "https://generativelanguage.googleapis.com/v1beta";

    // The scene pictures on the batch tier. The wire shape is the Gemini API's own, as Google's Gen AI .NET
    // SDK writes and reads it: create at models/{model}:batchGenerateContent with the requests under
    // batch.inputConfig.requests.requests[] (each a request and a metadata key); poll batches/{name}, whose
    // metadata.state is BATCH_STATE_*; the inline results are at metadata.output.inlinedResponses.inlinedResponses[],
    // each with its response or error and the metadata it was sent with.
    public async Task<string> SubmitSceneImages(IReadOnlyList<SceneImageJob> jobs, string artStyle, CancellationToken cancellationToken)
    {
        var requests = new JsonArray();
        foreach (var job in jobs)
        {
            requests.Add(new JsonObject
            {
                ["request"] = BuildRequest(
                    BuildSceneImagePrompt(artStyle, job.Prompt, job.References.Count > 0), job.References, job.AspectRatio),
                ["metadata"] = new JsonObject { ["key"] = $"scene-{job.SceneNumber}" },
            });
        }
        var payload = new JsonObject
        {
            ["batch"] = new JsonObject
            {
                ["displayName"] = "folio-scene-images",
                ["inputConfig"] = new JsonObject { ["requests"] = new JsonObject { ["requests"] = requests } },
            },
        };

        var url = $"{BaseUrl}/{options.Model}:batchGenerateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        var body = await Send(HttpMethod.Post, url, payload.ToJsonString(), cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } batch
            ? batch
            : throw new InvalidOperationException($"Gemini accepted a batch but named none: {body}");
    }

    public async Task<GeminiBatchPoll> PollSceneImages(string batchName, CancellationToken cancellationToken)
    {
        var url = $"{ApiRoot}/{batchName}?key={Uri.EscapeDataString(options.ApiKey)}";
        var body = await Send(HttpMethod.Get, url, null, cancellationToken);
        var root = JsonNode.Parse(body);
        var state = root?["metadata"]?["state"]?.GetValue<string>() ?? string.Empty;
        if (state is not ("BATCH_STATE_SUCCEEDED" or "BATCH_STATE_FAILED" or "BATCH_STATE_CANCELLED" or "BATCH_STATE_EXPIRED"))
            return new GeminiBatchPoll(false, []);

        var responses = (root?["metadata"]?["output"]?["inlinedResponses"]?["inlinedResponses"]
                         ?? root?["response"]?["inlinedResponses"]?["inlinedResponses"]) as JsonArray;
        var images = new List<BatchItem<byte[]>>();
        foreach (var item in responses ?? [])
        {
            var key = item?["metadata"]?["key"]?.GetValue<string>();
            if (key is null || !key.StartsWith("scene-", StringComparison.Ordinal)
                || !int.TryParse(key["scene-".Length..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number))
                continue;

            if (item?["error"] is { } error)
            {
                images.Add(new BatchItem<byte[]>(number, null, $"Gemini batch item failed: {error.ToJsonString()}"));
                continue;
            }

            var response = item?["response"]?.ToJsonString() ?? "{}";
            var image = TryExtractImage(response);
            images.Add(image is not null
                ? new BatchItem<byte[]>(number, image, null)
                : new BatchItem<byte[]>(number, null, ClassifyBlock(response)?.Message ?? "Gemini returned no image for this scene."));
        }
        healthSink?.MarkOk(CredentialIds.Gemini);
        return new GeminiBatchPoll(true, images);
    }

    private async Task<string> Send(HttpMethod method, string url, string? json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (json is not null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(CredentialIds.Gemini, $"{(int)response.StatusCode} {response.StatusCode}");
            throw ToException(response, body);
        }
        return body;
    }

    // One image request's body: the prompt, the reference sheets, an image back at the slot's shape. The same
    // for a single call and for an item of a batch.
    private static JsonObject BuildRequest(string prompt, IReadOnlyList<byte[]> referenceImages, double? aspectRatio)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = prompt } };
        foreach (var referenceImage in referenceImages)
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = "image/png",
                    ["data"] = Convert.ToBase64String(referenceImage)
                }
            });

        var generationConfig = new JsonObject { ["responseModalities"] = new JsonArray("IMAGE") };
        if (aspectRatio is { } ratio)
            generationConfig["imageConfig"] = new JsonObject { ["aspectRatio"] = NearestAspectRatio(ratio) };

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = parts }),
            ["generationConfig"] = generationConfig
        };
    }

    private async Task<byte[]> Generate(
        string prompt, IReadOnlyList<byte[]> referenceImages, double? aspectRatio,
        CancellationToken cancellationToken)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = prompt } };
        foreach (var referenceImage in referenceImages)
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = "image/png",
                    ["data"] = Convert.ToBase64String(referenceImage)
                }
            });

        var generationConfig = new JsonObject { ["responseModalities"] = new JsonArray("IMAGE") };
        // Ask the model to frame the image at the slot's aspect ratio, so it fills the slot rather than
        // being letterboxed onto shaded paper when placed in the report.
        if (aspectRatio is { } ratio)
            generationConfig["imageConfig"] = new JsonObject { ["aspectRatio"] = NearestAspectRatio(ratio) };

        var payload = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = parts }),
            ["generationConfig"] = generationConfig
        };

        var url = $"{BaseUrl}/{options.Model}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Only a credential rejection flags the key; throttling/5xx/network are transient and left alone.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(CredentialIds.Gemini, $"{(int)response.StatusCode} {response.StatusCode}");
            throw ToException(response, body);
        }

        // A 2xx means the key authenticated — clear any prior failure even if the content was then blocked.
        healthSink?.MarkOk(CredentialIds.Gemini);

        var image = TryExtractImage(body);
        if (image is not null)
            return image;

        // No image came back. A deliberate content block is surfaced as a typed exception so the worker
        // can retry a different moment of the scene; anything else (a text-only "sorry", an empty
        // candidate) stays an InvalidOperationException.
        var block = ClassifyBlock(body);
        if (block is not null)
            throw block;

        throw new InvalidOperationException($"Gemini response contained no image data: {body}");
    }

    private static Exception ToException(HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests || IsResourceExhausted(body))
        {
            // A 429 covers two very different conditions. A per-minute throttle clears on its own, so it
            // retries on the normal backoff. Depleted billing credits ("prepayment credits are depleted")
            // clear only when the user tops up, so retrying every few seconds just burns calls; it's
            // flagged QuotaExhausted so the worker waits a long interval and reports an actionable message.
            var quotaExhausted = IsCreditsExhausted(body);
            var label = quotaExhausted ? "quota exhausted" : "rate limited";
            return new RateLimitException(
                $"Gemini {label}: {(int)status} ({status}): {body}",
                response.Headers.RetryAfter?.Delta,
                quotaExhausted ? RateLimitKind.QuotaExhausted : RateLimitKind.Transient);
        }

        return new HttpRequestException(
            $"Gemini request failed: {(int)status} ({status}): {body}", inner: null, status);
    }

    // A 429 whose error message names billing/credits is a depleted-quota wall, not a short throttle:
    // it needs the user to add billing before any retry can succeed. Message wording is the only reliable
    // signal — the status is RESOURCE_EXHAUSTED for both — so match the terms Google uses for it.
    private static bool IsCreditsExhausted(string body)
    {
        var message = ErrorMessage(body);
        if (message is null)
            return false;
        return message.Contains("credit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("depleted", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: no message to classify by.
        }
        return null;
    }

    private static bool IsResourceExhausted(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("status", out var statusProperty))
            {
                return statusProperty.GetString() == "RESOURCE_EXHAUSTED";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: classify by status code only.
        }
        return false;
    }

    private static byte[]? TryExtractImage(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates))
            return null;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inline) &&
                    inline.TryGetProperty("data", out var data) &&
                    data.GetString() is { } base64)
                {
                    return Convert.FromBase64String(base64);
                }
            }
        }

        return null;
    }

    // Reads a no-image response for a deliberate content block and classifies it. Folio never relaxes
    // safety thresholds or asks the user to approve a render — on any block the worker simply retries a
    // different moment of the scene and, failing that, leaves the image unavailable — so the reason is
    // recorded for logging but the categories no longer matter. Returns null when the response carries no
    // block signal (a plain text-only reply).
    private static ImageContentBlockedException? ClassifyBlock(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        // A blocked prompt returns no candidates but a promptFeedback.blockReason.
        if (root.TryGetProperty("promptFeedback", out var feedback) &&
            feedback.TryGetProperty("blockReason", out var blockReason) &&
            blockReason.GetString() is { Length: > 0 } promptReason)
        {
            return promptReason == "SAFETY"
                ? new ImageContentBlockedException(
                    $"Gemini blocked the prompt on safety grounds: {responseJson}", ImageBlockReason.Safety)
                : new ImageContentBlockedException(
                    $"Gemini blocked the prompt as {promptReason}: {responseJson}", ImageBlockReason.Prohibited);
        }

        if (!root.TryGetProperty("candidates", out var candidates))
            return null;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("finishReason", out var finishReasonProperty) ||
                finishReasonProperty.GetString() is not { Length: > 0 } finishReason)
                continue;

            if (finishReason == "SAFETY")
                return new ImageContentBlockedException(
                    $"Gemini blocked the image on safety grounds: {responseJson}", ImageBlockReason.Safety);

            // IMAGE_SAFETY is Gemini's non-configurable image-output filter — recoverable by rephrasing.
            if (finishReason == "IMAGE_SAFETY")
                return new ImageContentBlockedException(
                    $"Gemini refused the image as {finishReason}: {responseJson}", ImageBlockReason.Filtered);

            // PROHIBITED_CONTENT, RECITATION, SPII, BLOCKLIST — hard content blocks.
            if (finishReason is "PROHIBITED_CONTENT" or "RECITATION" or "SPII" or "BLOCKLIST")
                return new ImageContentBlockedException(
                    $"Gemini refused the image as {finishReason}: {responseJson}", ImageBlockReason.Prohibited);
        }

        return null;
    }
}
