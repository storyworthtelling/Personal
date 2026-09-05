using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codex.Domain.Post;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Email;

/// <summary>
/// Asks Claude what a message is, and whether it proposes a date. One call for both,
/// because detection costs nothing extra once the message is being read and two calls for
/// one message would be two bills.
///
/// It is only asked about mail the rules could not place, which keeps most of the owner's
/// correspondence off the network entirely. The whole message goes when it does go — that
/// was the owner's explicit choice, on their own shared key.
///
/// <b>Every failure is silent.</b> No key, a rejected key, a timeout, a garbled reply: all
/// of them return "no idea" rather than throwing, because this runs inside the sync and a
/// sync that dies on a classification stops mail arriving at all. Rules-only is a working
/// Post; no mail is not.
/// </summary>
public sealed class MailReadingClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 400;

    // Bodies are truncated hard. The kind of a message and any date in it are decided in
    // the first screenful; the rest is signature, quoted history and legal boilerplate,
    // which costs tokens and adds nothing.
    private const int MaxBodyChars = 2_000;

    private const string Instruction =
        "You sort one email for a personal inbox and spot any date it proposes. Reply with ONLY a JSON " +
        "object: {\"kind\":string,\"event\":{\"title\":string,\"startsOn\":\"YYYY-MM-DD\",\"startsAt\":\"HH:MM\"}}. " +
        "kind is exactly one of: human, marketing, newsletter, bill, transactional. " +
        "human = written by a person to this person, including a person writing on behalf of a business. " +
        "marketing = promotional. newsletter = a subscribed periodical. bill = a statement, invoice or " +
        "payment request. transactional = a receipt, confirmation, code or notification. " +
        "Include \"event\" ONLY if the message proposes a specific dated occasion the recipient would attend; " +
        "omit it entirely otherwise, and never invent a date. Omit \"startsAt\" for an all-day occasion. " +
        "No prose outside the JSON.";

    private readonly HttpClient httpClient;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    public MailReadingClient(HttpClient httpClient, IOptions<VoiceOptions> options)
    {
        this.httpClient = httpClient;
        optionsAccessor = options;
    }

    public async Task<MailReading> ReadAsync(
        string from, string? subject, string? body, CancellationToken cancellation = default)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return MailReading.Nothing; // rules only, silently
        }

        try
        {
            var prompt = new StringBuilder()
                .AppendLine($"From: {from}")
                .AppendLine($"Subject: {subject}")
                .AppendLine()
                .Append(Truncate(body))
                .ToString();

            var payload = JsonSerializer.Serialize(new
            {
                model = options.FastModel,
                max_tokens = MaxTokens,
                system = Instruction,
                messages = new[] { new { role = "user", content = prompt } },
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", options.ApiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return MailReading.Nothing;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var text = document.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return MailReading.Parse(text);
        }
        catch (Exception)
        {
            // Deliberately broad. Anything at all going wrong here means "no idea", never
            // a failed sync — mail arriving matters more than mail being sorted.
            return MailReading.Nothing;
        }
    }

    private static string Truncate(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }
        return body.Length <= MaxBodyChars ? body : body[..MaxBodyChars];
    }
}
