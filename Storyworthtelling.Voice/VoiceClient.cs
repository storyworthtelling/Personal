using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Storyworthtelling.Voice;

// The Claude-backed voice client: it learns the owner's voice from the documents they upload
// (RefineAsync) and writes situational app messages in that voice (ComposeAsync). Kept a self-contained
// raw-HTTP client so the shared voice library depends on nothing from any one app — every
// com.storyworthtelling app hands it the Claude key its user already entered.
//
// These calls are short (a distilled profile, a one-sentence message), so unlike the repo's storyboard
// client this one is non-streaming: request, response, done. Failures propagate with a descriptive
// message; VoicedMessenger is the layer that turns a failure into a graceful fall back to neutral copy.
public sealed class VoiceClient : IVoiceClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string RefineInstruction =
        "You are a voice analyst distilling one author's writing voice so short app messages can later be " +
        "written to sound like them. You are given the CURRENT PROFILE (possibly empty) and a NEW WRITING " +
        "SAMPLE. Update the profile to reflect the sample, folding it into what's already there rather " +
        "than replacing it. Capture how the person actually writes — tone, register, attitude, cadence — " +
        "not the subject matter. Respond with ONLY a JSON object: " +
        "{\"summary\":string,\"traits\":[string],\"examples\":[string],\"avoid\":[string]}. " +
        "summary = a short paragraph describing the voice. traits = a few short descriptors (\"wry\", " +
        "\"warm\", \"economical\"). examples = a few characteristic phrasings drawn from the sample, to " +
        "anchor the voice concretely. avoid = things that would read as not-them (\"corporate " +
        "boilerplate\", \"exclamation marks\", \"hype\"). Keep each list to at most 6 items. No prose " +
        "outside the JSON.";

    private const string ComposeInstruction =
        "You are writing a single situational message inside an app, in the voice described by the PROFILE " +
        "below. The message is UI copy shown for a specific occasion — write it to sound like the person " +
        "the profile describes, honoring their traits and avoiding what they'd avoid. PURPOSE names the " +
        "occasion; CONTEXT gives the specifics to reflect. Keep it within the word limit, plain text, no " +
        "markup, no quotes, no preamble — just the message itself.";

    private readonly HttpClient httpClient;
    private readonly VoiceOptions options;

    public VoiceClient(HttpClient httpClient, VoiceOptions options)
    {
        this.httpClient = httpClient;
        this.options = options;
    }

    public async Task<VoiceProfile> RefineAsync(
        VoiceProfile existing, string documentText, CancellationToken cancellationToken)
    {
        var existingJson = JsonSerializer.Serialize(new
        {
            summary = existing.Summary,
            traits = existing.Traits,
            examples = existing.Examples,
            avoid = existing.Avoid
        });
        var userContent = $"CURRENT PROFILE (JSON):\n{existingJson}\n\nNEW WRITING SAMPLE:\n{documentText}";

        var reply = await SendMessage(
            RefineInstruction, userContent, options.Model, options.RefineMaxTokens, cancellationToken);

        var json = ExtractObject(reply);
        if (json is null)
            return existing; // a garbled reply must never corrupt a good profile

        ProfileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProfileDto>(json, CaseInsensitive);
        }
        catch (JsonException)
        {
            return existing;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Summary))
            return existing;

        // Provenance is counted here rather than trusted to the model: one more document has now fed the
        // profile than fed the one we passed in.
        return new VoiceProfile(
            dto.Summary.Trim(),
            dto.Traits ?? [],
            dto.Examples ?? [],
            dto.Avoid ?? [],
            existing.DocumentsAnalyzed + 1);
    }

    public async Task<string> ComposeAsync(
        VoiceProfile profile, MessageRequest request, CancellationToken cancellationToken)
    {
        var profileJson = JsonSerializer.Serialize(new
        {
            summary = profile.Summary,
            traits = profile.Traits,
            examples = profile.Examples,
            avoid = profile.Avoid
        });
        var context = string.IsNullOrWhiteSpace(request.Context) ? "(none)" : request.Context;
        var userContent =
            $"PROFILE (JSON):\n{profileJson}\n\n" +
            $"PURPOSE: {request.Purpose}\n" +
            $"CONTEXT: {context}\n" +
            $"WORD LIMIT: {request.MaxWords}";

        var reply = await SendMessage(
            ComposeInstruction, userContent, options.FastModel, options.ComposeMaxTokens, cancellationToken);
        return reply.Trim();
    }

    private async Task<string> SendMessage(
        string instruction, string userText, string model, int maxTokens, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            max_tokens = maxTokens,
            system = instruction,
            messages = new[] { new { role = "user", content = userText } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic voice request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}");

        return ExtractText(body);
    }

    // Concatenates the text blocks from a non-streaming Messages response.
    private static string ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                block.TryGetProperty("text", out var chunk))
                text.Append(chunk.GetString());
        }
        return text.ToString();
    }

    // Pulls the first balanced {...} object out of a reply, so a model that wraps its JSON in prose still
    // parses. Returns null when there's no object to find.
    private static string? ExtractObject(string reply)
    {
        var start = reply.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < reply.Length; i++)
        {
            var c = reply[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return reply[start..(i + 1)];
                    break;
            }
        }
        return null;
    }

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private sealed class ProfileDto
    {
        public string? Summary { get; set; }
        public List<string>? Traits { get; set; }
        public List<string>? Examples { get; set; }
        public List<string>? Avoid { get; set; }
    }
}
