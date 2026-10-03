using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Foreword.Domain.Calendar;
using Foreword.Domain.Health;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Handwriting;

// Transcribes the hand-written notes in the bottom region of a delivered document into
// structured notes by sending the annotated PDF to the Anthropic Messages API. The static
// instruction prefix is prompt-cached; the daily PDF is not.
public class ClaudeVisionHandwritingExtractor : IHandwritingExtractor
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string Instruction =
        "You are reading a daily planner page that was printed and then hand-written on with a " +
        "stylus. Ignore all printed/typed text and the printed grid. Look ONLY at the hand-written " +
        "ink in the 'write below' region near the bottom of the page. Transcribe each hand-written " +
        "future event or task. Respond with ONLY a JSON array (no prose). Each element: " +
        "{\"text\": string, \"when\": ISO-8601 date or date-time or null, " +
        "\"column\": one of the column names given with the page, exactly as written, or null, " +
        "\"isTask\": boolean (true for a to-do, false for a calendar event), " +
        "\"confidence\": number 0..1 for how confident you are in the transcription}. " +
        "If the region is empty, return []. Use low confidence when the writing is hard to read.";

    private readonly HttpClient httpClient;
    private readonly AnthropicOptions options;
    private readonly ILogger<ClaudeVisionHandwritingExtractor> logger;
    private readonly ICredentialHealthSink? healthSink;
    private readonly IOptions<CalendarMappingOptions>? mapping;

    // The column names are this account's own (set up in Foreword), so they reach the model in the
    // message rather than the cached instruction: the instruction stays one cached prefix whatever
    // anybody's columns are called, and the code never holds a name.
    public ClaudeVisionHandwritingExtractor(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeVisionHandwritingExtractor> logger,
        ICredentialHealthSink? healthSink = null,
        IOptions<CalendarMappingOptions>? mapping = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
        this.healthSink = healthSink;
        this.mapping = mapping;
    }

    private IReadOnlyList<PersonColumn> Columns =>
        (mapping?.Value.Layout() ?? ColumnLayout.Unset).Printed;

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
                    text = Instruction,
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
                            text = $"Transcribe the hand-written notes. The columns are: {string.Join(", ", Columns)}."
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
                healthSink?.MarkFailing(CredentialIds.Anthropic, $"{(int)response.StatusCode} {response.StatusCode}");
            throw new HttpRequestException(
                $"Anthropic request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}",
                inner: null,
                response.StatusCode);
        }

        healthSink?.MarkOk(CredentialIds.Anthropic);
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
                Column = ParseColumn(GetString(item, "column")),
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

    // Only one of this account's columns. A name the model offers that is not one reads as no column,
    // and the note goes where an unnamed note goes, rather than into a column the page does not print.
    private PersonColumn? ParseColumn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var named = new PersonColumn(value);
        return Columns.Contains(named) ? Columns.First(c => c == named) : null;
    }
}
