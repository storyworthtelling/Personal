using Storyworthtelling.Voice.Claude;
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
    private const string DefaultUserMessage = "Transcribe the hand-written notes.";

    private readonly string instruction;
    private readonly Func<string>? userMessage;
    private readonly ClaudeMessagesClient claude;
    private readonly ClaudeVisionOptions options;
    private readonly ILogger<ClaudeVisionHandwritingExtractor> logger;

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
        this.options = options.Value;
        this.logger = logger;
        claude = new ClaudeMessagesClient(httpClient, () => options.Value.ApiKey, healthSink, credentialId);
        this.userMessage = userMessage;
    }

    public async Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf)
    {
        if (annotatedPdf.Length == 0)
            return [];

        var request = new ClaudeRequest(options.Model, 1024,
            [ClaudeBlock.Text(instruction, cache: true)],
            [ClaudeBlock.Pdf(Convert.ToBase64String(annotatedPdf)), ClaudeBlock.Text(userMessage?.Invoke() ?? DefaultUserMessage)]);
        var reply = await claude.SendAsync(request, CancellationToken.None);
        return ParseNotes(reply.Text);
    }

    private IReadOnlyList<ParsedNote> ParseNotes(string text)
    {

        var arrayJson = ExtractJsonArray(text);
        if (arrayJson is null)
        {
            // The reply is a transcription of the owner's handwriting, so only its size is logged.
            logger.LogWarning("Handwriting response contained no JSON array ({Length} characters)", text.Length);
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
