using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codex.Domain.Apps;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Apps;

/// <summary>Generates an engraved-bookend glyph for an owner-created shelf (feature #11).</summary>
public interface IShelfGlyphClient
{
    /// <summary>An SVG path (0–100 viewBox) for the shelf name, or null to fall back to the monogram.</summary>
    Task<string?> GenerateAsync(string shelfName, CancellationToken cancellationToken);
}

/// <summary>
/// The Claude-backed shelf-glyph client. Reuses the shared <see cref="VoiceOptions"/> key + cheap
/// model tier (like the Taste/Discovery clients) to turn a shelf name into a single monochrome SVG
/// path the head engraves on the bronze bookend. It is <em>self-degrading</em>: no key, an HTTP
/// failure, or a reply that doesn't validate as a lone path all return null, and the head then
/// draws the offline pencil monogram (<see cref="ShelfMonogram"/>) instead — so shelf creation
/// never blocks on the model.
/// </summary>
public sealed class ShelfGlyphClient : IShelfGlyphClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string Instruction =
        "You design tiny solid bronze statues for a bookshelf's category labels — a small figure that " +
        "stands on the shelf as the bookend itself. Given a shelf name, respond with ONLY the SVG path " +
        "data (the value of a single \"d\" attribute) for one simple, iconic SILHOUETTE representing " +
        "that name: a single CLOSED, FILLABLE shape (end with Z), not a line drawing or open strokes — " +
        "it will be rendered solid-filled, so an outline that doesn't enclose a real area will render " +
        "as a blob or nothing. Authored inside a 0 0 100 100 viewBox. No <svg> wrapper, no attributes, " +
        "no code fence, no words — just the path data beginning with M. Keep the silhouette simple and " +
        "legible at small size.";

    private readonly HttpClient httpClient;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    public ShelfGlyphClient(HttpClient httpClient, IOptions<VoiceOptions> options)
    {
        this.httpClient = httpClient;
        optionsAccessor = options;
    }

    public async Task<string?> GenerateAsync(string shelfName, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(shelfName))
        {
            return null; // no key or nothing to draw — the head uses the monogram
        }

        try
        {
            var payload = new
            {
                model = options.FastModel,
                max_tokens = 400,
                system = Instruction,
                messages = new[] { new { role = "user", content = $"Shelf name: {shelfName}" } },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
            request.Headers.Add("x-api-key", options.ApiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return GlyphPath.Extract(ExtractText(body));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }
        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                block.TryGetProperty("text", out var chunk))
            {
                text.Append(chunk.GetString());
            }
        }
        return text.ToString();
    }
}
