using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.External;

// Image generation via the Gemini (Generative Language) API. Uses an image-capable model that
// accepts reference images, so character/setting reference sheets can be supplied on every panel
// render to keep appearance consistent. RESOURCE_EXHAUSTED / 429 surface as RateLimitException.
public sealed class GeminiImageClient : IGeminiImageClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient httpClient;
    private readonly GeminiOptions options;

    public GeminiImageClient(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken)
    {
        var prompt =
            $"Character/location reference sheet in this style: {artStyle}. " +
            $"Render a clear, well-lit, full reference view (neutral background) of: {description}. " +
            "Single subject, consistent canonical appearance, no text or lettering.";
        return Generate(prompt, referenceImages: [], aspectRatio: null, relaxedCategories: [], cancellationToken);
    }

    public Task<byte[]> GeneratePanel(
        string visualPrompt, string artStyle, IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio, CancellationToken cancellationToken) =>
        GeneratePanel(visualPrompt, artStyle, referenceImages, aspectRatio, relaxedCategories: [], cancellationToken);

    public Task<byte[]> GeneratePanel(
        string visualPrompt, string artStyle, IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio, IReadOnlyCollection<string> relaxedCategories, CancellationToken cancellationToken)
    {
        var prompt = PanelImagePrompt.Build(artStyle, visualPrompt, referenceImages.Count > 0);
        return Generate(prompt, referenceImages, aspectRatio, relaxedCategories, cancellationToken);
    }

    // The aspect ratios gemini-2.5-flash-image accepts; the requested panel aspect is snapped to the
    // nearest so the request never carries an unsupported value.
    private static readonly (string Label, double Ratio)[] SupportedAspectRatios =
    {
        ("21:9", 21.0 / 9), ("16:9", 16.0 / 9), ("3:2", 3.0 / 2), ("4:3", 4.0 / 3), ("5:4", 5.0 / 4),
        ("1:1", 1.0), ("4:5", 4.0 / 5), ("3:4", 3.0 / 4), ("2:3", 2.0 / 3), ("9:16", 9.0 / 16),
    };

    private static string NearestAspectRatio(double ratio) =>
        SupportedAspectRatios.MinBy(candidate => Math.Abs(candidate.Ratio - ratio)).Label;

    private async Task<byte[]> Generate(
        string prompt, IReadOnlyList<byte[]> referenceImages, double? aspectRatio,
        IReadOnlyCollection<string> relaxedCategories, CancellationToken cancellationToken)
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
        // Ask the model to frame the image at the panel's aspect ratio, so it fills the slot rather than
        // being letterboxed onto shaded paper when composed.
        if (aspectRatio is { } ratio)
            generationConfig["imageConfig"] = new JsonObject { ["aspectRatio"] = NearestAspectRatio(ratio) };

        var payload = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = parts }),
            ["generationConfig"] = generationConfig
        };

        // safetySettings is emitted ONLY for an approved re-render (relaxedCategories non-empty), and
        // then only for the flagged categories. Omitting it entirely keeps the default render at
        // Google's default thresholds — the app never lowers them on the normal path.
        if (relaxedCategories.Count > 0)
        {
            var safetySettings = new JsonArray();
            foreach (var category in relaxedCategories)
                safetySettings.Add(new JsonObject { ["category"] = category, ["threshold"] = "BLOCK_NONE" });
            payload["safetySettings"] = safetySettings;
        }

        var url = $"{BaseUrl}/{options.Model}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ToException(response, body);

        var image = TryExtractImage(body);
        if (image is not null)
            return image;

        // No image came back. A deliberate content block (safety or prohibited) is surfaced as a typed
        // exception the worker can branch on; anything else (a text-only "sorry", an empty candidate)
        // stays an InvalidOperationException as before.
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
            return new RateLimitException(
                $"Gemini rate limited: {(int)status} ({status}): {body}",
                response.Headers.RetryAfter?.Delta);
        }

        return new HttpRequestException(
            $"Gemini request failed: {(int)status} ({status}): {body}", inner: null, status);
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

    // The adjustable Gemini harm categories. When a SAFETY block names no specific category we relax
    // all of them on the approved re-render, since only the user-approved single call is affected.
    private static readonly string[] AdjustableCategories =
    {
        "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH",
        "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT"
    };

    // Reads a no-image response for a deliberate content block and classifies it. A SAFETY block is
    // adjustable — the flagged categories are collected so only those get relaxed on re-render. Every
    // other block reason (prohibited/copyrighted content, recitation, blocklist) is non-adjustable.
    // Returns null when the response carries no block signal (a plain text-only reply).
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
                    $"Gemini blocked the prompt on safety grounds: {responseJson}",
                    ImageBlockReason.Safety, CollectFlaggedCategories(feedback))
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
                    $"Gemini blocked the image on safety grounds: {responseJson}",
                    ImageBlockReason.Safety, CollectFlaggedCategories(candidate));

            // PROHIBITED_CONTENT, IMAGE_SAFETY, RECITATION, SPII, BLOCKLIST — none of them adjustable.
            if (finishReason is "PROHIBITED_CONTENT" or "IMAGE_SAFETY" or "RECITATION" or "SPII" or "BLOCKLIST")
                return new ImageContentBlockedException(
                    $"Gemini refused the image as {finishReason}: {responseJson}", ImageBlockReason.Prohibited);
        }

        return null;
    }

    // The HARM_CATEGORY_* names a SAFETY block flagged — those marked blocked, else those above a low
    // probability. Falls back to all adjustable categories when the ratings don't pinpoint one.
    private static IReadOnlyList<string> CollectFlaggedCategories(JsonElement container)
    {
        if (!container.TryGetProperty("safetyRatings", out var ratings) || ratings.ValueKind != JsonValueKind.Array)
            return AdjustableCategories;

        var flagged = new List<string>();
        foreach (var rating in ratings.EnumerateArray())
        {
            if (rating.TryGetProperty("category", out var categoryProperty) &&
                categoryProperty.GetString() is { Length: > 0 } category)
            {
                var blocked = rating.TryGetProperty("blocked", out var blockedProperty) &&
                              blockedProperty.ValueKind == JsonValueKind.True;
                var probability = rating.TryGetProperty("probability", out var probabilityProperty)
                    ? probabilityProperty.GetString()
                    : null;
                if (blocked || probability is "MEDIUM" or "HIGH")
                    flagged.Add(category);
            }
        }

        return flagged.Count > 0 ? flagged : AdjustableCategories;
    }
}
