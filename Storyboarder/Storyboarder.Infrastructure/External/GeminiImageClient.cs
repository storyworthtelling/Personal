using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.External;
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
        return Generate(prompt, referenceImages: [], cancellationToken);
    }

    public Task<byte[]> GeneratePanel(
        string visualPrompt, string artStyle, IReadOnlyList<byte[]> referenceImages,
        CancellationToken cancellationToken)
    {
        var prompt =
            $"Comic panel in this style: {artStyle}. {visualPrompt}. " +
            (referenceImages.Count > 0
                ? "Keep the characters and settings visually consistent with the supplied reference images. "
                : string.Empty) +
            "Do not render any text, speech bubbles, or lettering in the image.";
        return Generate(prompt, referenceImages, cancellationToken);
    }

    private async Task<byte[]> Generate(
        string prompt, IReadOnlyList<byte[]> referenceImages, CancellationToken cancellationToken)
    {
        var parts = new List<object> { new { text = prompt } };
        foreach (var image in referenceImages)
            parts.Add(new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(image) } });

        var payload = new
        {
            contents = new[] { new { parts } },
            generationConfig = new { responseModalities = new[] { "IMAGE" } }
        };

        var url = $"{BaseUrl}/{options.Model}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ToException(response, body);

        return ExtractImage(body);
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

    private static byte[] ExtractImage(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates))
            throw new InvalidOperationException($"Gemini response contained no candidates: {responseJson}");

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

        throw new InvalidOperationException($"Gemini response contained no image data: {responseJson}");
    }
}
