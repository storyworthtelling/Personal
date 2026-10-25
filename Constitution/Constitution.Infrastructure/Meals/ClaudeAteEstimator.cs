using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.Meals;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// "I ate something else" asked of the vision model over the Messages API (C-6.6.2). One request: the fixed instruction
/// (<see cref="AtePrompt.System"/>), the photograph, and the fixed question (<see cref="AtePrompt.Question"/>). The key is one request
/// header and the model is the shared Claude vision option.
///
/// <para><b>What leaves the phone:</b> that one photograph, with its EXIF (place, time, device) taken out first
/// (<see cref="JpegScrub"/>) and its size bounded, and the two fixed texts. No name, no time of day, no meal, no Body reading and no
/// earlier estimate is added (D98, D65). The photograph is read here from a path, held in a local for the length of the call, and
/// the caller deletes the file; nothing is stored on this object.</para>
///
/// <para><b>It never guesses.</b> No key is <see cref="AteOutcome.Unavailable"/> before anything is read; a photograph that is not a
/// whole JPEG, or is too big, is <see cref="AteOutcome.Unreadable"/> and is not sent; a reply that makes no sense is
/// <see cref="AteOutcome.Unreadable"/> (<see cref="AteReply"/>). Transport and service failures are exceptions carrying the status,
/// and the credential-health report is told only what the service said about the key.</para>
/// </summary>
public sealed class ClaudeAteEstimator(
    HttpClient http,
    IOptions<ClaudeVisionOptions> options,
    Func<string, byte[]> readPhoto,
    ICredentialHealthSink? health = null) : IAteEstimator
{
    /// <summary>The largest photograph that is sent. The service takes five megabytes; this leaves room and keeps a mobile upload small.</summary>
    public const int LargestPhotoBytes = 4_000_000;

    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaximumReplyTokens = 600;
    private const int LongestReason = 200;

    private readonly ClaudeVisionOptions settings = options.Value;

    public async Task<AteOutcome> EstimateAsync(string photoPath, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoPath);

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new AteOutcome.Unavailable(AteText.NoKeySentence);
        }

        var bytes = readPhoto(photoPath);
        if (bytes.Length == 0 || bytes.Length > LargestPhotoBytes || JpegScrub.WithoutMetadata(bytes) is not { } picture)
        {
            return new AteOutcome.Unreadable();
        }

        var payload = new
        {
            model = settings.Model,
            max_tokens = MaximumReplyTokens,
            system = new[] { new { type = "text", text = AtePrompt.System, cache_control = new { type = "ephemeral" } } },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(picture) } },
                        new { type = "text", text = AtePrompt.Question },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", settings.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
            {
                health?.MarkFailing(CredentialIds.Claude, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            throw new HttpRequestException(
                $"The meal estimate was refused by the service: {(int)response.StatusCode} ({response.StatusCode}){ReasonFrom(body)}", null, response.StatusCode);
        }

        health?.MarkOk(CredentialIds.Claude);
        return AteReply.Parse(TextFrom(body));
    }

    private static string? TextFrom(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("type", out var type) && type.GetString() == "text"
                        && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            // A body that is not JSON holds no answer; the estimate is "could not estimate".
            return null;
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
        }
        catch (JsonException)
        {
            // Not a JSON error body (a gateway's plain text, say); the status alone has to do.
        }

        return string.Empty;
    }
}
