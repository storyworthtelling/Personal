using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Claude;

/// <summary>The suite's model tiers, named once (audit 01 §4): six option classes each wrote them out.</summary>
public static class ClaudeModels
{
    /// <summary>The everyday tier: reading, sorting, composing.</summary>
    public const string Standard = "claude-sonnet-5-5";

    /// <summary>The quick, cheap tier: one-line copy, tiny drawings.</summary>
    public const string Fast = "claude-haiku-5-5";

    /// <summary>The careful tier, for Folio's readers.</summary>
    public const string Strong = "claude-opus-5-5";
}

/// <summary>One content block: text, a base64 image or a base64 PDF, optionally marked for the prompt cache.</summary>
public sealed record ClaudeBlock(string Kind, string Value, string? MediaType, bool Cache)
{
    public static ClaudeBlock Text(string text, bool cache = false) => new("text", text, null, cache);

    public static ClaudeBlock Image(string mediaType, string base64, bool cache = false) => new("image", base64, mediaType, cache);

    public static ClaudeBlock Pdf(string base64, bool cache = false) => new("document", base64, "application/pdf", cache);
}

/// <summary>One request: a model, an output ceiling, a system prompt and the user's turn.</summary>
public sealed record ClaudeRequest(string Model, int MaxTokens, IReadOnlyList<ClaudeBlock> System, IReadOnlyList<ClaudeBlock> User)
{
    /// <summary>The common case: a plain system prompt and a plain user message, nothing cached.</summary>
    public static ClaudeRequest Plain(string model, int maxTokens, string system, string user) =>
        new(model, maxTokens, [ClaudeBlock.Text(system)], [ClaudeBlock.Text(user)]);
}

public sealed record ClaudeUsage(int InputTokens, int OutputTokens, int CacheWriteTokens, int CacheReadTokens);

/// <summary>The reply's text blocks joined, how it stopped, what it cost and the request id the API gave it.</summary>
public sealed record ClaudeReply(string Text, string? StopReason, ClaudeUsage Usage, string? RequestId);

public enum ClaudeFailure
{
    /// <summary>No key on this phone; nothing was sent.</summary>
    NoKey,

    /// <summary>401/403: the key was refused. Credential health is told, so its settings field comes back.</summary>
    KeyRejected,

    /// <summary>429: a short, self-clearing throttle.</summary>
    RateLimited,

    /// <summary>529 or overloaded_error: the service is busy, not this request's fault.</summary>
    Overloaded,

    /// <summary>The account's credit balance is spent; only the owner can clear it.</summary>
    QuotaExhausted,

    Failed,
}

/// <summary>
/// A request that did not succeed, and why. It is an <see cref="HttpRequestException"/>, so every caller's
/// existing catch still holds; <see cref="Failure"/> is what a caller that cares reads.
/// </summary>
public sealed class ClaudeRequestException(
    ClaudeFailure failure, string message, HttpStatusCode? status = null, TimeSpan? retryAfter = null,
    string? responseBody = null, string? requestId = null)
    : HttpRequestException(message, null, status)
{
    public ClaudeFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>What the API said. Its error bodies name the problem and never echo the key.</summary>
    public string? ResponseBody { get; } = responseBody;

    public string? RequestId { get; } = requestId;
}

/// <summary>
/// The one Messages API client (audit 01 §4). Each app keeps its prompts and its parsing; this owns the URL,
/// the version header, the request shape, the text-block walk, credential health (Ok on success, failing on a
/// refused key) and telling a refused key, a throttle, an overload and an empty balance apart. The key is read
/// live on every call, so rotating it in the vault reaches the next request.
/// </summary>
public sealed class ClaudeMessagesClient(
    HttpClient http, Func<string?> apiKey, ICredentialHealthSink? health = null, string credentialId = SharedCredentialIds.Claude)
{
    public const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";

    public bool HasKey => !string.IsNullOrWhiteSpace(apiKey());

    public async Task<ClaudeReply> SendAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ClaudeRequestException(ClaudeFailure.NoKey, "There is no Claude key on this phone.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", ApiVersion);
        message.Content = new StringContent(Body(request), Encoding.UTF8, "application/json");
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;

        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response, body, requestId);
        }

        health?.MarkOk(credentialId);
        return Read(body, requestId);
    }

    /// <summary>The request JSON. A lone uncached text block goes as a plain string, as every client sent it before.</summary>
    public static string Body(ClaudeRequest request)
    {
        var root = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens,
        };
        if (request.System.Count > 0)
        {
            root["system"] = Content(request.System);
        }

        root["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = Content(request.User) });
        return root.ToJsonString();
    }

    private static JsonNode Content(IReadOnlyList<ClaudeBlock> blocks)
    {
        if (blocks is [{ Kind: "text", Cache: false } only])
        {
            return JsonValue.Create(only.Value)!;
        }

        var array = new JsonArray();
        foreach (var block in blocks)
        {
            var node = block.Kind == "text"
                ? new JsonObject { ["type"] = "text", ["text"] = block.Value }
                : new JsonObject
                {
                    ["type"] = block.Kind,
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = block.MediaType, ["data"] = block.Value },
                };
            if (block.Cache)
            {
                node["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
            }

            array.Add(node);
        }

        return array;
    }

    private ClaudeRequestException Failure(HttpResponseMessage response, string body, string? requestId)
    {
        var status = (int)response.StatusCode;
        var described = $"Claude request failed: {status} ({response.StatusCode}){(requestId is null ? "" : $", request {requestId}")}: {body}";
        var retryAfter = response.Headers.RetryAfter?.Delta;

        if (AuthFailure.IsHttpCredentialRejection(status, body))
        {
            health?.MarkFailing(credentialId, $"Claude refused the key ({status}).");
            return new ClaudeRequestException(ClaudeFailure.KeyRejected, described, response.StatusCode, null, body, requestId);
        }

        var kind = status switch
        {
            429 => ClaudeFailure.RateLimited,
            529 => ClaudeFailure.Overloaded,
            _ when body.Contains("overloaded_error", StringComparison.Ordinal) => ClaudeFailure.Overloaded,
            400 when body.Contains("credit balance", StringComparison.OrdinalIgnoreCase) => ClaudeFailure.QuotaExhausted,
            _ => ClaudeFailure.Failed,
        };
        return new ClaudeRequestException(kind, described, response.StatusCode, retryAfter, body, requestId);
    }

    private static ClaudeReply Read(string body, string? requestId)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ClaudeReply(string.Empty, null, new ClaudeUsage(0, 0, 0, 0), requestId);
        }

        var text = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                    block.TryGetProperty("text", out var chunk))
                {
                    text.Append(chunk.GetString());
                }
            }
        }

        var stop = root.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object
            ? new ClaudeUsage(Count(u, "input_tokens"), Count(u, "output_tokens"), Count(u, "cache_creation_input_tokens"), Count(u, "cache_read_input_tokens"))
            : new ClaudeUsage(0, 0, 0, 0);
        return new ClaudeReply(text.ToString(), stop, usage, requestId);
    }

    private static int Count(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}

/// <summary>
/// Which Claude key an app uses (audit 01 §3): the shared vault's, read live, so a key rotated once reaches every
/// app's next call. A key an app still holds locally from before (Foreword's old separate key, a copy Folio or
/// Constitution made once) stands in only while the vault has none, so no phone stops working on the update.
/// </summary>
public static class ClaudeKeySource
{
    public static string Choose(string? shared, string? local) =>
        !string.IsNullOrWhiteSpace(shared) ? shared.Trim() : local?.Trim() ?? string.Empty;
}
