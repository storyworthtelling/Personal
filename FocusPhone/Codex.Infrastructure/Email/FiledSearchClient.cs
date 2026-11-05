using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codex.Domain.Post;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Email;

/// <summary>Claude's reply to one Filed search ask, with what it cost.</summary>
public sealed record FiledSearchReply(string? Text, FiledUsage Usage, string? StopReason, string? RequestId);

/// <summary>A Filed search ask that Anthropic refused or that could not be read.</summary>
public sealed class FiledSearchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The one call behind a Filed search. The instruction and catalogue go as a cached system block,
/// so "show more" re-reads them at a tenth of the price; the query and what has been shown go in
/// the user turn. It runs on the strong tier: this is judgement over the owner's own words, and a
/// search is a deliberate, occasional act rather than something that runs on every sync.
///
/// Unlike the mail sorter this throws, with what Anthropic said: the screen falls back to the
/// word match either way, but the log should say why Claude was not used.
/// </summary>
public sealed class FiledSearchClient(HttpClient httpClient, IOptions<VoiceOptions> options)
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 400;

    public bool HasKey => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public string Model => options.Value.Model;

    public async Task<FiledSearchReply> AskAsync(
        FiledCatalogue catalogue, string query, IReadOnlyList<int> shownLines, DateOnly today,
        CancellationToken cancellation = default)
    {
        var current = options.Value;
        var payload = JsonSerializer.Serialize(new
        {
            model = current.Model,
            max_tokens = MaxTokens,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = FiledSearchPrompt.System(catalogue),
                    cache_control = new { type = "ephemeral" },
                },
            },
            messages = new[] { new { role = "user", content = FiledSearchPrompt.Ask(query, shownLines, today) } },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", current.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        var requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
        if (!response.IsSuccessStatusCode)
        {
            throw new FiledSearchException(
                $"Anthropic refused the Filed search ({(int)response.StatusCode}, request {requestId ?? "unknown"}): {ErrorMessage(body)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var text = root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray()
                    .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
                    .Select(block => block.GetProperty("text").GetString())
                    .FirstOrDefault()
                : null;
            var stop = root.TryGetProperty("stop_reason", out var reason) ? reason.GetString() : null;
            return new FiledSearchReply(text, Usage(root), stop, requestId);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FiledSearchException(
                $"The Filed search reply could not be read (request {requestId ?? "unknown"})", e);
        }
    }

    private static FiledUsage Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return new FiledUsage(0, 0, 0, 0);
        }
        int Count(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;
        return new FiledUsage(
            Count("input_tokens"), Count("cache_read_input_tokens"),
            Count("cache_creation_input_tokens"), Count("output_tokens"));
    }

    // Anthropic's error body names the problem without echoing the key.
    private static string ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body.Length <= 200 ? body : body[..200];
        }
    }
}
