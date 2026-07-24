using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.External;

// Literary analysis via the Anthropic Messages API. Modeled on the repo's existing Claude client:
// x-api-key + anthropic-version headers, a prompt-cached system instruction, and descriptive
// failures. Provider throttling is surfaced as RateLimitException so the worker can reschedule.
public sealed class ClaudeAnalysisClient : IClaudeAnalysisClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string ExtractionInstruction =
        "You are a visual-development analyst preparing a graphic-novel design bible. Read the novel " +
        "excerpt and list every distinct CHARACTER and every distinct SETTING/LOCATION that appears, " +
        "with concrete visual details useful for an illustrator (appearance, clothing, age, build for " +
        "characters; architecture, era, mood, palette for settings). For each entity also give its " +
        "approximate physical SCALE so the illustrations stay proportionate: for a character an " +
        "approximate height and build relative to an average adult; for a setting its approximate size " +
        "and any notable scale cues. Respond with ONLY a JSON object: " +
        "{\"characters\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]," +
        "\"settings\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]}. " +
        "Use [] when none are present, and \"\" for an unknown scale. No prose outside the JSON.";

    private const string StoryboardInstruction =
        "You are a comics writer adapting a novel into a graphic novel. Using the supplied design bible " +
        "for consistency, break the provided passage into as many volumes, pages, and panels as the " +
        "story needs. Aim for roughly 3 to 6 panels per page (never more than 6), and give a pivotal " +
        "beat its own page. For each panel give a vivid visual prompt that captures a SPECIFIC MOMENT " +
        "OF ONGOING ACTION — the characters caught mid-gesture or mid-movement, interacting with each " +
        "other and the setting — with an explicit shot type or camera angle (e.g. wide establishing " +
        "shot, low-angle close-up, over-the-shoulder); avoid static, posed, portrait, or " +
        "looking-at-camera framing. Respect the relative scale of the entities given in the design " +
        "bible. Put no lettering in the art; give any dialogue and caption to be typeset separately, " +
        "and the names of design-bible entities present. Maintain continuity with the running summary. " +
        "Respond with ONLY a JSON object: " +
        "{\"continuitySummary\":string,\"volumes\":[{\"number\":int,\"title\":string,\"pages\":" +
        "[{\"chapterNumber\":int,\"panels\":[{\"visualPrompt\":string,\"dialogue\":string|null," +
        "\"caption\":string|null,\"referenceNames\":[string]}]}]}]}. No prose outside the JSON.";

    private const string ChapterAnalysisInstruction =
        "You are a developmental editor analyzing ONE excerpt of a novel manuscript to help the author " +
        "revise toward publication. Use the KNOWN CAST names consistently when they appear. Read the " +
        "excerpt and record concise, concrete, accurate observations. Respond with ONLY a JSON object: " +
        "{\"charactersPresent\":[string],\"keyEvents\":[string],\"characterBeats\":[string]," +
        "\"setupsAndPayoffs\":[string],\"possibleInconsistencies\":[string],\"factualClaims\":[string]," +
        "\"notableStrengths\":[string],\"notableWeaknesses\":[string]}. characterBeats = decisions, " +
        "changes, or motivation shifts; setupsAndPayoffs = planted elements or promises (note if paid " +
        "off here); factualClaims = checkable real-world statements that may need fact-checking; " +
        "possibleInconsistencies = anything that may contradict earlier text (names, timeline, physical " +
        "details, world rules). Use [] when none are present. No prose outside the JSON.";

    private const string ConsolidationInstruction =
        "You are a panel of professional readers producing a REVISION REPORT for an author preparing a " +
        "novel for publication. You are given per-chunk notes gathered across the whole manuscript in " +
        "reading order, plus the cast. Synthesize the WHOLE book (do not merely concatenate the notes); " +
        "be accurate, fair, specific, and constructive. Respond with ONLY a JSON object: " +
        "{\"logline\":string,\"synopsis\":string,\"characters\":[{\"name\":string,\"role\":string," +
        "\"arcSummary\":string,\"relationships\":[string]}],\"themes\":[string],\"plotHoles\":[string]," +
        "\"inconsistencies\":[string],\"inaccuracies\":[string],\"pacingNotes\":[string],\"reviews\":" +
        "[{\"perspective\":string,\"assessment\":string,\"strengths\":[string],\"weaknesses\":[string]," +
        "\"recommendation\":string}],\"revisionPlan\":[{\"priority\":string,\"issue\":string," +
        "\"suggestedFix\":string}],\"comparableTitles\":[string],\"marketPositioning\":string}. " +
        "Provide AT LEAST THREE reviews from DISTINCT, fair perspectives (e.g. developmental editor, " +
        "target-genre reader, literary critic, acquiring agent) — differing but honest, each with real " +
        "strengths and weaknesses. Order revisionPlan HIGHEST-IMPACT FIRST and set priority to " +
        "\"High\", \"Medium\", or \"Low\". Use [] when none. No prose outside the JSON.";

    private const string ImplyInstruction =
        "You are a comics director revising a single panel's visual prompt. The image model refused to " +
        "draw it because some element is too explicit to depict. Rewrite the prompt so that same beat is " +
        "IMPLIED rather than shown: turn the camera away, cut to reaction or aftermath, obscure the " +
        "explicit element with foreground/shadow/framing, or move it just off-panel — while keeping the " +
        "mood, characters, and story moment intact. Keep it a single vivid panel description with no " +
        "lettering. Respond with ONLY the rewritten visual prompt as plain text — no preamble, no quotes.";

    private readonly HttpClient httpClient;
    private readonly ClaudeOptions options;

    public ClaudeAnalysisClient(HttpClient httpClient, IOptions<ClaudeOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken)
    {
        var reply = await SendMessage(ExtractionInstruction, chunkText, cancellationToken);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return DesignBibleFragment.Empty;

        var dto = JsonResponse.Deserialize<FragmentDto>(json);
        if (dto is null)
            return DesignBibleFragment.Empty;

        return new DesignBibleFragment(MapMentions(dto.Characters), MapMentions(dto.Settings));
    }

    public async Task<StoryboardSegment> Storyboard(
        string textWindow, string continuitySummary, DesignBible bible, string artStyle,
        CancellationToken cancellationToken)
    {
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        // The art style and design bible are identical for every window of a novel, so they go in a
        // cached block: consecutive windows and any retries then bill this as a cache read (~0.1x)
        // rather than re-sending the whole bible at full input price on every call.
        var cachedContext =
            $"ART STYLE (apply to every visual prompt): {artStyle}\n\n" +
            $"DESIGN BIBLE (JSON):\n{bibleJson}";
        var userContent =
            $"CONTINUITY SO FAR:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none yet)" : continuitySummary)}\n\n" +
            $"PASSAGE TO ADAPT:\n{textWindow}";

        var reply = await SendMessage(
            StoryboardInstruction, userContent, cancellationToken,
            maxTokens: options.StoryboardMaxTokens, cachedContext: cachedContext);
        var json = JsonResponse.ExtractObject(reply)
            ?? throw new InvalidOperationException("Claude storyboard reply did not contain a JSON object.");

        var dto = JsonResponse.Deserialize<StoryboardDto>(json)
            ?? throw new InvalidOperationException("Claude storyboard reply could not be parsed.");

        var volumes = (dto.Volumes ?? [])
            .Select(v => new VolumeDraft(
                v.Number,
                v.Title ?? string.Empty,
                (v.Pages ?? []).Select(MapPage).ToList()))
            .ToList();

        return new StoryboardSegment(volumes, dto.ContinuitySummary ?? string.Empty);
    }

    public async Task<ChapterNotes> AnalyzeChapter(
        string chapterText, IReadOnlyList<string> characterNames, CancellationToken cancellationToken)
    {
        var cast = characterNames.Count == 0 ? "(none identified yet)" : string.Join(", ", characterNames);
        var userContent = $"KNOWN CAST: {cast}\n\nEXCERPT:\n{chapterText}";

        var reply = await SendMessage(ChapterAnalysisInstruction, userContent, cancellationToken);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return ChapterNotes.Empty;

        var dto = JsonResponse.Deserialize<ChapterNotesDto>(json);
        if (dto is null)
            return ChapterNotes.Empty;

        return new ChapterNotes(
            dto.CharactersPresent ?? [], dto.KeyEvents ?? [], dto.CharacterBeats ?? [],
            dto.SetupsAndPayoffs ?? [], dto.PossibleInconsistencies ?? [], dto.FactualClaims ?? [],
            dto.NotableStrengths ?? [], dto.NotableWeaknesses ?? []);
    }

    public async Task<BookAnalysisResult> ConsolidateAnalysis(
        string title, IReadOnlyList<ChapterNotes> notes, DesignBible bible, string continuitySummary,
        CancellationToken cancellationToken)
    {
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        var userContent =
            $"TITLE: {title}\n\n" +
            $"CAST (design bible JSON):\n{bibleJson}\n\n" +
            $"CONTINUITY SUMMARY:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none)" : continuitySummary)}\n\n" +
            $"PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        var reply = await SendMessage(ConsolidationInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return BookAnalysisResult.Empty;

        var dto = JsonResponse.Deserialize<BookAnalysisDto>(json);
        if (dto is null)
            return BookAnalysisResult.Empty;

        return new BookAnalysisResult(
            dto.Logline ?? string.Empty,
            dto.Synopsis ?? string.Empty,
            (dto.Characters ?? []).Select(c =>
                new CharacterProfile(c.Name ?? string.Empty, c.Role ?? string.Empty, c.ArcSummary ?? string.Empty, c.Relationships ?? [])).ToList(),
            dto.Themes ?? [],
            dto.PlotHoles ?? [],
            dto.Inconsistencies ?? [],
            dto.Inaccuracies ?? [],
            dto.PacingNotes ?? [],
            (dto.Reviews ?? []).Select(r =>
                new Review(r.Perspective ?? string.Empty, r.Assessment ?? string.Empty, r.Strengths ?? [], r.Weaknesses ?? [], r.Recommendation ?? string.Empty)).ToList(),
            (dto.RevisionPlan ?? []).Select(r =>
                new RevisionItem(r.Priority ?? string.Empty, r.Issue ?? string.Empty, r.SuggestedFix ?? string.Empty)).ToList(),
            dto.ComparableTitles ?? [],
            dto.MarketPositioning ?? string.Empty);
    }

    public async Task<string> RewritePromptToImply(string visualPrompt, CancellationToken cancellationToken)
    {
        var reply = await SendMessage(ImplyInstruction, visualPrompt, cancellationToken);
        var rewritten = reply.Trim();
        // Fall back to the original prompt if the model returned nothing usable, so the caller always
        // has a prompt to try rather than an empty one.
        return string.IsNullOrWhiteSpace(rewritten) ? visualPrompt : rewritten;
    }

    private async Task<string> SendMessage(
        string instruction, string userText, CancellationToken cancellationToken,
        int? maxTokens = null, string? cachedContext = null)
    {
        // Invariant, reused context (the design bible) is cached as its own block so repeated calls
        // within the cache window bill it as a cache read instead of full input. Both blocks share one
        // anonymous shape so the array stays strongly typed (no object-typed JSON serialization).
        var system = string.IsNullOrEmpty(cachedContext)
            ? new[]
            {
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } }
            }
            : new[]
            {
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } },
                new { type = "text", text = cachedContext, cache_control = new { type = "ephemeral" } }
            };

        var effectiveMaxTokens = maxTokens ?? options.MaxTokens;
        var payload = new
        {
            model = options.Model,
            max_tokens = effectiveMaxTokens,
            stream = true,
            system,
            messages = new[]
            {
                new { role = "user", content = userText }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(payload);

        // Stream the response as server-sent events. A large storyboard generation runs well past the
        // default 100s HttpClient timeout, and a non-streaming request leaves the socket idle for those
        // minutes — mobile carriers/NATs then abort it, surfacing as "Socket closed" / "Software caused
        // connection abort". Streaming keeps bytes flowing so the connection stays alive, and reading
        // headers first means the long generation isn't bounded by the send timeout.
        var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ToException(response, body);
        }

        return await ReadStreamedText(response, effectiveMaxTokens, cancellationToken);
    }

    // Accumulates the assistant's text from an Anthropic Messages streaming response. Only text_delta
    // events contribute; a mid-stream error event is surfaced with the same classification as an HTTP
    // error so the worker reschedules throttling rather than crashing the cycle. The message_delta
    // event carries the final stop_reason: if the model ran out of output budget ("max_tokens") the
    // accumulated text is truncated, so we raise instead of returning a partial (unparseable) body.
    private static async Task<string> ReadStreamedText(
        HttpResponseMessage response, int maxTokens, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        string? stopReason = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0)
                continue;

            using var evt = JsonDocument.Parse(data);
            var root = evt.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
                continue;

            switch (typeProperty.GetString())
            {
                case "content_block_delta"
                    when root.TryGetProperty("delta", out var delta) &&
                         delta.TryGetProperty("type", out var deltaType) &&
                         deltaType.GetString() == "text_delta" &&
                         delta.TryGetProperty("text", out var chunk):
                    text.Append(chunk.GetString());
                    break;

                case "message_delta"
                    when root.TryGetProperty("delta", out var messageDelta) &&
                         messageDelta.TryGetProperty("stop_reason", out var reason) &&
                         reason.ValueKind == JsonValueKind.String:
                    stopReason = reason.GetString();
                    break;

                case "error":
                    throw StreamError(root);

                case "message_stop":
                    return Finish(text, stopReason, maxTokens);
            }
        }

        return Finish(text, stopReason, maxTokens);
    }

    private static string Finish(StringBuilder text, string? stopReason, int maxTokens)
    {
        if (stopReason == "max_tokens")
            throw new ResponseTruncatedException(
                $"Claude response was truncated at the {maxTokens}-token output limit " +
                "(stop_reason=max_tokens); the response is incomplete. Increase the request's " +
                "max_tokens budget.");

        return text.ToString();
    }

    private static Exception StreamError(JsonElement root)
    {
        var body = root.GetRawText();
        var type = root.TryGetProperty("error", out var error) && error.TryGetProperty("type", out var t)
            ? t.GetString()
            : null;

        if (type is "rate_limit_error" or "overloaded_error")
            return new RateLimitException($"Anthropic rate limited (stream): {body}");

        return new HttpRequestException($"Anthropic stream error: {body}");
    }

    private static Exception ToException(HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests || (int)status == 529 || IsRateLimitBody(body))
        {
            return new RateLimitException(
                $"Anthropic rate limited: {(int)status} ({status}): {body}",
                response.Headers.RetryAfter?.Delta);
        }

        return new HttpRequestException(
            $"Anthropic request failed: {(int)status} ({status}): {body}", inner: null, status);
    }

    private static bool IsRateLimitBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("type", out var type))
            {
                var value = type.GetString();
                return value is "rate_limit_error" or "overloaded_error";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: fall back to status-code classification.
        }
        return false;
    }

    private static IReadOnlyList<EntityMention> MapMentions(List<EntityDto>? entities) =>
        (entities ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new EntityMention(e.Name!, e.Description ?? string.Empty, e.Scale ?? string.Empty, e.Aliases ?? []))
            .ToList();

    private static PageDraft MapPage(PageDto page) =>
        new(page.ChapterNumber,
            (page.Panels ?? [])
                .Select(p => new PanelDraft(
                    p.VisualPrompt ?? string.Empty, p.Dialogue, p.Caption, p.ReferenceNames ?? []))
                .ToList());

    private sealed class FragmentDto
    {
        public List<EntityDto>? Characters { get; set; }
        public List<EntityDto>? Settings { get; set; }
    }

    private sealed class EntityDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Scale { get; set; }
        public List<string>? Aliases { get; set; }
    }

    private sealed class StoryboardDto
    {
        public string? ContinuitySummary { get; set; }
        public List<VolumeDto>? Volumes { get; set; }
    }

    private sealed class VolumeDto
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public List<PageDto>? Pages { get; set; }
    }

    private sealed class PageDto
    {
        public int ChapterNumber { get; set; }
        public List<PanelDto>? Panels { get; set; }
    }

    private sealed class PanelDto
    {
        public string? VisualPrompt { get; set; }
        public string? Dialogue { get; set; }
        public string? Caption { get; set; }
        public List<string>? ReferenceNames { get; set; }
    }

    private sealed class ChapterNotesDto
    {
        public List<string>? CharactersPresent { get; set; }
        public List<string>? KeyEvents { get; set; }
        public List<string>? CharacterBeats { get; set; }
        public List<string>? SetupsAndPayoffs { get; set; }
        public List<string>? PossibleInconsistencies { get; set; }
        public List<string>? FactualClaims { get; set; }
        public List<string>? NotableStrengths { get; set; }
        public List<string>? NotableWeaknesses { get; set; }
    }

    private sealed class BookAnalysisDto
    {
        public string? Logline { get; set; }
        public string? Synopsis { get; set; }
        public List<CharacterProfileDto>? Characters { get; set; }
        public List<string>? Themes { get; set; }
        public List<string>? PlotHoles { get; set; }
        public List<string>? Inconsistencies { get; set; }
        public List<string>? Inaccuracies { get; set; }
        public List<string>? PacingNotes { get; set; }
        public List<ReviewDto>? Reviews { get; set; }
        public List<RevisionItemDto>? RevisionPlan { get; set; }
        public List<string>? ComparableTitles { get; set; }
        public string? MarketPositioning { get; set; }
    }

    private sealed class CharacterProfileDto
    {
        public string? Name { get; set; }
        public string? Role { get; set; }
        public string? ArcSummary { get; set; }
        public List<string>? Relationships { get; set; }
    }

    private sealed class ReviewDto
    {
        public string? Perspective { get; set; }
        public string? Assessment { get; set; }
        public List<string>? Strengths { get; set; }
        public List<string>? Weaknesses { get; set; }
        public string? Recommendation { get; set; }
    }

    private sealed class RevisionItemDto
    {
        public string? Priority { get; set; }
        public string? Issue { get; set; }
        public string? SuggestedFix { get; set; }
    }
}
