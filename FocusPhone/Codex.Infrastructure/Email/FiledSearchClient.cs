using System.Text.Json;
using Codex.Domain.Post;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice.Health;

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
public sealed class FiledSearchClient(HttpClient httpClient, IOptions<VoiceOptions> options, ICredentialHealthSink? health = null)
{
    private readonly ClaudeMessagesClient claude = new(httpClient, () => options.Value.ApiKey, health);
    private const int MaxTokens = 400;

    public bool HasKey => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public string Model => options.Value.Model;

    public async Task<FiledSearchReply> AskAsync(
        FiledCatalogue catalogue, string query, IReadOnlyList<int> shownLines, DateOnly today,
        CancellationToken cancellation = default)
    {
        var current = options.Value;
        var request = new ClaudeRequest(current.Model, MaxTokens,
            System: [ClaudeBlock.Text(FiledSearchPrompt.System(catalogue), cache: true)],
            User: [ClaudeBlock.Text(FiledSearchPrompt.Ask(query, shownLines, today))]);

        ClaudeReply reply;
        try
        {
            reply = await claude.SendAsync(request, cancellation);
        }
        catch (ClaudeRequestException e)
        {
            throw new FiledSearchException(
                $"Anthropic refused the Filed search ({(int?)e.StatusCode}, request {e.RequestId ?? "unknown"}): {ErrorMessage(e.ResponseBody ?? e.Message)}", e);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FiledSearchException("The Filed search reply could not be read", e);
        }

        var usage = reply.Usage;
        return new FiledSearchReply(
            reply.Text.Length == 0 ? null : reply.Text,
            new FiledUsage(usage.InputTokens, usage.CacheReadTokens, usage.CacheWriteTokens, usage.OutputTokens),
            reply.StopReason, reply.RequestId);
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
