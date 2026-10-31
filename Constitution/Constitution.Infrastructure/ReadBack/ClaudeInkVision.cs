using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.ReadBack;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The vision read over the Messages API, the way the shared handwriting extractor makes the same call (the page sent as
/// a PDF document, the standing instruction prompt-cached) with the one difference that matters here: it needs the answer
/// per box, mark and confidence included, which the shared extractor's note shape cannot carry and which drops a box
/// with no words. The key and the model are the shared Claude options (<see cref="ClaudeVisionOptions"/>); the key goes in
/// one request header and nowhere else.
///
/// <para><b>What leaves the phone:</b> the cloud's PDF of the page, the standing instruction (<see cref="InkInstruction.System"/>)
/// and the list of boxes (<see cref="InkInstruction.Request"/>). No name and no Body reading is added beside it (D98). The PDF itself prints that
/// day's plan and, if there is one, the sore place, and carries whatever the owner wrote, the notes margin among it: that is the design of a vision read of the page, and
/// the request asks about the listed boxes only.</para>
///
/// <para><b>A reply that makes no sense is not an error.</b> It comes back as pages not read (<see cref="InkReply"/>), which
/// keeps the page and tries again. Transport and service failures are exceptions with the status; the credential-health
/// report is told only what the service said about the key (a rejection, or a success), never about throttling or an outage.</para>
/// </summary>
public sealed class ClaudeInkVision(HttpClient http, IOptions<ClaudeVisionOptions> options, ICredentialHealthSink? health = null) : IInkVision
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // A page of a dozen boxes is a few hundred tokens of reply; this leaves room for a long meal sheet.
    private const int MaximumReplyTokens = 4096;
    private const int LongestReason = 200;

    private readonly ClaudeVisionOptions settings = options.Value;

    public async Task<InkDocumentRead> Read(ExpectedDocument document, byte[] annotatedPdf, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotatedPdf);
        if (!document.ReadsInk)
        {
            throw new ArgumentException("This document has no boxes to read, so there is nothing to send.", nameof(document));
        }

        if (annotatedPdf.Length == 0)
        {
            throw new ArgumentException("The tablet returned an empty file, so there is no page to send.", nameof(annotatedPdf));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("The Claude key is not set, so the page cannot be read yet. It is read again once the key is in place.");
        }

        var payload = new
        {
            model = settings.Model,
            max_tokens = MaximumReplyTokens,
            system = new[] { new { type = "text", text = InkInstruction.System, cache_control = new { type = "ephemeral" } } },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "document", source = new { type = "base64", media_type = "application/pdf", data = Convert.ToBase64String(annotatedPdf) } },
                        new { type = "text", text = InkInstruction.Request(document) },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", settings.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
            {
                health?.MarkFailing(CredentialIds.Claude, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            throw new HttpRequestException(
                $"The ink read was refused by the service: {(int)response.StatusCode} ({response.StatusCode}){ReasonFrom(body)}", null, response.StatusCode);
        }

        health?.MarkOk(CredentialIds.Claude);
        return InkReply.Parse(TextFrom(body), document);
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
            // A body that is not JSON holds no answer; the pages come back not read.
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
