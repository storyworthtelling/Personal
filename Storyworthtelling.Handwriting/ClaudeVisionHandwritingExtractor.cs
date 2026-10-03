using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Handwriting;

// Transcribes the hand-written notes in a region of a PDF page into structured notes by sending
// the page to the Anthropic Messages API. The static instruction prefix is prompt-cached; the
// page itself is not.
//
// What region of the page to read, what the columns/categories mean, and what to return is
// entirely the caller's own instruction text, supplied at construction — this type owns only the
// HTTP call, the caching, the credential-health reporting, and the tolerant JSON-array parsing.
// Moved out of Foreword (Index-P11 Release F): Foreword supplies its own wording, and the names of
// its columns beside each page (userMessage), and a second caller supplies its own.
public class ClaudeVisionHandwritingExtractor : IHandwritingExtractor
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string DefaultUserMessage = "Transcribe the hand-written notes.";

    private readonly string instruction;
    private readonly string credentialId;
    private readonly Func<string>? userMessage;
    private readonly HttpClient httpClient;
    private readonly ClaudeVisionOptions options;
    private readonly ILogger<ClaudeVisionHandwritingExtractor> logger;
    private readonly ICredentialHealthSink? healthSink;

    // userMessage is the text that rides beside the page, read afresh on every Extract. Anything
    // that varies per account or per day (the names of the columns the page prints, say) belongs
    // here rather than in the instruction: the instruction is the prompt-cached prefix and stays
    // one cached prefix whatever the caller's data is, and this library never has to know it.
    // Omitted, the message is the plain "Transcribe the hand-written notes."
    public ClaudeVisionHandwritingExtractor(
        string instruction,
        string credentialId,
        HttpClient httpClient,
        IOptions<ClaudeVisionOptions> options,
        ILogger<ClaudeVisionHandwritingExtractor> logger,
        ICredentialHealthSink? healthSink = null,
        Func<string>? userMessage = null)
    {
        this.instruction = instruction;
        this.credentialId = credentialId;
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
        this.healthSink = healthSink;
        this.userMessage = userMessage;
    }

    public async Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf)
    {
        if (annotatedPdf.Length == 0)
            return [];

        var payload = new
        {
            model = options.Model,
            max_tokens = 1024,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = instruction,
                    cache_control = new { type = "ephemeral" }
                }
            },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "document",
                            source = new
                            {
                                type = "base64",
                                media_type = "application/pdf",
                                data = Convert.ToBase64String(annotatedPdf)
                            }
                        },
                        new
                        {
                            type = "text",
                            text = userMessage?.Invoke() ?? DefaultUserMessage
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(credentialId, $"{(int)response.StatusCode} {response.StatusCode}");
            throw new HttpRequestException(
                $"Anthropic request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}",
                inner: null,
                response.StatusCode);
        }

        healthSink?.MarkOk(credentialId);
        var json = await response.Content.ReadAsStringAsync();
        return ParseNotes(json);
    }

    private IReadOnlyList<ParsedNote> ParseNotes(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .FirstOrDefault(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
            .GetProperty("text")
            .GetString() ?? string.Empty;

        var arrayJson = ExtractJsonArray(text);
        if (arrayJson is null)
        {
            logger.LogWarning("Handwriting response contained no JSON array: {Text}", text);
            return [];
        }

        var notes = new List<ParsedNote>();
        using var parsed = JsonDocument.Parse(arrayJson);
        foreach (var item in parsed.RootElement.EnumerateArray())
        {
            var noteText = GetString(item, "text");
            if (string.IsNullOrWhiteSpace(noteText))
                continue;

            notes.Add(new ParsedNote
            {
                Text = noteText,
                When = ParseDate(GetString(item, "when")),
                Column = GetString(item, "column"),
                IsTask = item.TryGetProperty("isTask", out var t) && t.ValueKind == JsonValueKind.True,
                Confidence = item.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var conf) ? conf : 0
            });
        }

        return notes;
    }

    // Pulls the first top-level JSON array out of the model's reply, tolerating any stray prose.
    private static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;
}
