using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

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
        "{\"register\":string,\"traits\":[string],\"examples\":[string],\"avoid\":[string]," +
        "\"cadence\":[string],\"punctuation\":[string]}. " +
        "register = how this person sounds, in a few words rather than a paragraph (\"wry, warm, " +
        "economical\"). traits = a few short descriptors (\"plain-spoken\", \"unhurried\", \"dry\"). " +
        "examples = 2 or 3 characteristic phrasings drawn from THIS sample, quoted closely, to anchor the " +
        "voice concretely — supply only fresh ones from this sample; do NOT repeat the profile's existing " +
        "examples, as the app preserves those itself. cadence = the shapes their sentences take (\"short " +
        "declarative openers\", \"lists of three\", \"a long sentence then a very short one\"). " +
        "punctuation = their marks and how they use them (\"em-dash asides\", \"semicolons, rarely\", " +
        "\"no exclamation marks\"). Keep cadence and punctuation apart: a sentence shape and a mark are " +
        "different instructions to write under. avoid = things that would read as not-them (\"corporate " +
        "boilerplate\", \"hype\"). Keep traits, avoid, cadence and punctuation to at most 6 items each. " +
        "No prose outside the JSON.";

    private const string ComposeInstruction =
        "You are writing a single situational message inside an app, in the voice described by the PROFILE " +
        "below. The message is UI copy shown for a specific occasion — write it to sound like the person " +
        "the profile describes, honoring their traits, writing in their cadence and punctuation, drawing " +
        "on their " +
        "example phrasings, and avoiding what they'd avoid. PURPOSE names the occasion; CONTEXT gives the " +
        "specifics to reflect. Keep it within the word limit, plain text, no markup, no quotes, no " +
        "preamble — just the message itself.";

    private readonly HttpClient httpClient;
    // Read fresh on each call (not cached) so a head that supplies live options — the API key the user
    // enters in-app after launch — is seen without a restart, the same way the rest of the repo works.
    private readonly IOptions<VoiceOptions> optionsAccessor;

    public VoiceClient(HttpClient httpClient, IOptions<VoiceOptions> options)
    {
        this.httpClient = httpClient;
        optionsAccessor = options;
    }

    public async Task<VoiceProfile> RefineAsync(
        VoiceProfile existing, string documentText, Guid projectId, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        var existingJson = JsonSerializer.Serialize(new
        {
            register = existing.Register,
            traits = existing.Traits,
            // The model is shown the phrasings, not the book each came from: which book taught an
            // anchor is the app's bookkeeping for discarding it later, and nothing the model should
            // be reasoning about while it reads.
            examples = existing.Examples.Select(example => example.Text),
            avoid = existing.Avoid,
            cadence = existing.Cadence,
            punctuation = existing.Punctuation
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

        if (dto is null || string.IsNullOrWhiteSpace(dto.Register))
            return existing;

        // Provenance is counted here rather than trusted to the model: one more document has now fed the
        // profile than fed the one we passed in. Examples are merged (never replaced) here rather than
        // trusted to the model, so the concrete voice anchors accumulate across every book.
        return existing with
        {
            Register = dto.Register.Trim(),
            Traits = dto.Traits ?? [],
            Examples = MergeExamples(existing.Examples, dto.Examples, projectId),
            Avoid = dto.Avoid ?? [],
            Cadence = dto.Cadence ?? [],
            Punctuation = dto.Punctuation ?? [],
            DocumentsAnalyzed = existing.DocumentsAnalyzed + 1,
            // Whatever was read here is in the current shape, so there is nothing left to relearn.
            NeedsRelearn = false,
        };
    }

    // Folds the sample's fresh examples onto the ones already learned, keeping every old example (they are
    // the accumulated voice anchors) and appending any new ones the model returned. Trimmed, blanks
    // dropped, deduped case-insensitively so a phrase the model happens to echo isn't stored twice.
    private static IReadOnlyList<VoiceExample> MergeExamples(
        IReadOnlyList<VoiceExample> existing, List<string>? fresh, Guid projectId)
    {
        var merged = new List<VoiceExample>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in existing)
        {
            var trimmed = example.Text.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                merged.Add(example with { Text = trimmed });
        }

        // A phrasing this book happens to echo from an earlier one keeps the earlier book's id: the
        // anchor was already learned, and re-attributing it would let discarding this book take an
        // example the other one is still paying for.
        foreach (var text in fresh ?? [])
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                merged.Add(new VoiceExample(trimmed, projectId));
        }

        return merged;
    }

    public async Task<string> ComposeAsync(
        VoiceProfile profile, MessageRequest request, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        var profileJson = JsonSerializer.Serialize(new
        {
            register = profile.Register,
            traits = profile.Traits,
            examples = profile.Examples.Select(example => example.Text),
            avoid = profile.Avoid,
            cadence = profile.Cadence,
            punctuation = profile.Punctuation
        });
        var context = string.IsNullOrWhiteSpace(request.Context) ? "(none)" : request.Context;
        var userContent =
            $"PROFILE (JSON):\n{profileJson}\n\n" +
            $"PURPOSE: {request.Purpose}\n" +
            $"CONTEXT: {context}\n" +
            $"WORD LIMIT: {request.MaxWords}";

        // A message the owner actually reads for its voice runs on the strong model, where cadence and wit
        // survive; the many low-stakes messages stay on the cheap tier.
        var model = request.HighVisibility ? options.Model : options.FastModel;
        var reply = await SendMessage(
            ComposeInstruction, userContent, model, options.ComposeMaxTokens, cancellationToken);
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
        request.Headers.Add("x-api-key", optionsAccessor.Value.ApiKey);
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
        public string? Register { get; set; }
        public List<string>? Traits { get; set; }
        public List<string>? Examples { get; set; }
        public List<string>? Avoid { get; set; }
        public List<string>? Cadence { get; set; }
        public List<string>? Punctuation { get; set; }
    }
}
