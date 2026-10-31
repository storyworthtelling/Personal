using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Whether a written reason is a lasting lesson about someone's taste or a remark about one evening, asked of the text model over the Messages API (D133). One request:
/// the fixed instruction (<see cref="TasteJudgePrompt.System"/>) and one user message of the dish name and the written reason. The key is one request header and the
/// model is the shared Claude option; both are read from the options when the call is made, so a key restored from the vault since the last call takes effect on the next.
///
/// <para><b>What leaves the phone:</b> the dish name (the card's title, looked up from the card the evidence names; "none" when there is no card or the box no longer
/// holds it) and the words, capped. No person, no id, no birthdate, no roster, no calendar entry, no profile, no Body reading and no other evidence (D98); this class
/// never touches <see cref="TasteEvidence.Person"/> and holds no roster. Nothing is stored on it.</para>
///
/// <para><b>It never invents a judgement.</b> No key is <see cref="AuthorUnavailable.NotConfigured"/> before anything is sent. The reply is read only by
/// <see cref="TasteJudgeReply"/> (strict); a refusal, a malformed or over-long reply is <see cref="AuthorUnavailable.ReplyUnusable"/> and the words stay pending. A failure
/// is a typed <see cref="TasteJudgement.Unavailable"/> carrying a specific sentence (status and the service's own short reason; never the request, the words or the key),
/// and the original exception is kept as its cause so the stack survives. The credential-health report is told only what the service said about the key (401 or 403);
/// cancellation by the caller propagates.</para>
/// </summary>
public sealed class ClaudeTasteJudge(HttpClient http, IOptions<ClaudeVisionOptions> options, Func<RecipeId, string?> dishName, ICredentialHealthSink? health = null) : ITasteJudge
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaximumReplyTokens = 400;
    private const int LongestReason = 200;

    public async Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        cancellation.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new TasteJudgement.Unavailable(AuthorUnavailable.NotConfigured, "No Claude key is set, so the written words were not judged.");
        }

        var dish = evidence.Recipe is { } card ? dishName(card) : null;
        var payload = new Dictionary<string, object>
        {
            ["model"] = settings.Model,
            ["max_tokens"] = MaximumReplyTokens,
            ["system"] = new[] { new { type = "text", text = TasteJudgePrompt.System } },
            ["messages"] = new[] { new { role = "user", content = new[] { new { type = "text", text = TasteJudgePrompt.User(dish, evidence.Words) } } } },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        message.Headers.Add("x-api-key", settings.ApiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = JsonContent.Create(payload);

        string body;
        System.Net.HttpStatusCode status;
        try
        {
            using var response = await http.SendAsync(message, cancellation).ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException timeout) when (!cancellation.IsCancellationRequested)
        {
            // The client's own timeout, not the caller stopping: the service did not answer in time.
            return new TasteJudgement.Unavailable(AuthorUnavailable.TimedOut, "The taste judge did not answer in time.", timeout);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            return new TasteJudgement.Unavailable(AuthorUnavailable.Offline, $"The taste judge could not reach the service ({failure.GetType().Name}).", failure);
        }

        if ((int)status is < 200 or > 299)
        {
            var refused = $"The taste judge was refused by the service: {(int)status} ({status}){ReasonFrom(body)}";
            if (AuthFailure.IsHttpCredentialRejection((int)status, body))
            {
                health?.MarkFailing(CredentialIds.Claude, $"{(int)status} {status}");
                return new TasteJudgement.Unavailable(AuthorUnavailable.KeyRejected, refused);
            }

            return new TasteJudgement.Unavailable(AuthorUnavailable.Offline, refused);
        }

        health?.MarkOk(CredentialIds.Claude);
        var (text, declined) = ReplyFrom(body);
        return declined
            ? new TasteJudgement.Unavailable(AuthorUnavailable.ReplyUnusable, "The service declined to judge these words.")
            : TasteJudgeReply.Parse(text, evidence.Recipe);
    }

    // The first text block, and whether the service stopped because it declined.
    private static (string? Text, bool Declined) ReplyFrom(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, false);
            }

            var declined = parsed.RootElement.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String && stop.GetString() == "refusal";
            if (parsed.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("type", out var type) && type.GetString() == "text"
                        && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return (text.GetString(), declined);
                    }
                }
            }

            return (null, declined);
        }
        catch (JsonException)
        {
            // A body that is not JSON holds no answer; the reply is then unusable and the words stay pending.
            return (null, false);
        }
    }

    // The service's own words for why, when it gave some, capped; never the request.
    private static string ReasonFrom(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } reason)
            {
                return ": " + (reason.Length > LongestReason ? reason[..LongestReason] : reason);
            }

            return string.Empty;
        }
        catch (JsonException)
        {
            // Not a JSON error body (a gateway's plain text, say); the status alone has to do.
            return string.Empty;
        }
    }
}
