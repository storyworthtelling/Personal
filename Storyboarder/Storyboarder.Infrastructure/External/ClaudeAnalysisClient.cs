using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        "characters; architecture, era, mood, palette for settings). Respond with ONLY a JSON object: " +
        "{\"characters\":[{\"name\":string,\"description\":string,\"aliases\":[string]}]," +
        "\"settings\":[{\"name\":string,\"description\":string,\"aliases\":[string]}]}. " +
        "Use [] when none are present. No prose outside the JSON.";

    private const string StoryboardInstruction =
        "You are a comics writer adapting a novel into a graphic novel. Using the supplied design bible " +
        "for consistency, break the provided passage into as many volumes, pages, and panels as the " +
        "story needs. For each panel give a vivid visual prompt (no lettering in the art), plus any " +
        "dialogue and caption to be typeset separately, and the names of design-bible entities present. " +
        "Maintain continuity with the running summary. Respond with ONLY a JSON object: " +
        "{\"continuitySummary\":string,\"volumes\":[{\"number\":int,\"title\":string,\"pages\":" +
        "[{\"chapterNumber\":int,\"panels\":[{\"visualPrompt\":string,\"dialogue\":string|null," +
        "\"caption\":string|null,\"referenceNames\":[string]}]}]}]}. No prose outside the JSON.";

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
        var userContent =
            $"ART STYLE (apply to every visual prompt): {artStyle}\n\n" +
            $"DESIGN BIBLE (JSON):\n{bibleJson}\n\n" +
            $"CONTINUITY SO FAR:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none yet)" : continuitySummary)}\n\n" +
            $"PASSAGE TO ADAPT:\n{textWindow}";

        var reply = await SendMessage(StoryboardInstruction, userContent, cancellationToken);
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

    private async Task<string> SendMessage(string instruction, string userText, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = options.Model,
            max_tokens = options.MaxTokens,
            system = new[]
            {
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } }
            },
            messages = new[]
            {
                new { role = "user", content = userText }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ToException(response, body);

        return ExtractText(body);
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

    private static string ExtractText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .FirstOrDefault(block => block.TryGetProperty("type", out var t) && t.GetString() == "text")
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private static IReadOnlyList<EntityMention> MapMentions(List<EntityDto>? entities) =>
        (entities ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new EntityMention(e.Name!, e.Description ?? string.Empty, e.Aliases ?? []))
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
}
