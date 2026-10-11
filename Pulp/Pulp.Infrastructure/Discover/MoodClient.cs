using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;

namespace Pulp.Infrastructure.Discover;

/// <summary>Discover's one model seam: a mood search (D171) and a model-made playlist (D104).</summary>
public interface IMoodClient
{
    Task<MoodAnswer> AskMoodAsync(MoodQuery query, CancellationToken cancellationToken);

    Task<MoodAnswer> AskPlaylistAsync(PlaylistQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// The Claude-backed mood client, shaped like Score's <c>DiscoveryClient</c> and the shared
/// <see cref="VoiceClient"/>: raw, non-streaming HTTP to the Messages API, the key and the model
/// tiers read live from the shared <see cref="VoiceOptions"/> on every call (so a key the vault
/// restores is used by the next search). The answer is asked for as a JSON object and read
/// defensively; every failure — no key, a refused key, an unreachable service, a garbled reply —
/// comes back as "no idea" with the reason for the diagnostics log, never as a throw and never as
/// anything voiced. Only a refused key is reported to credential health; trouble that is not the
/// key's fault is not. Nothing it sends names a title anyone watched, a viewer or a child: what it
/// sends is exactly <see cref="MoodQuery"/> or <see cref="PlaylistQuery"/>, which cannot hold one.
/// </summary>
public sealed class MoodClient : IMoodClient
{
    /// <summary>The shared Claude key's credential id, the same one Folio and Index report under.</summary>
    public const string CredentialId = "claude";

    // The Messages endpoint and version, as VoiceClient and Score's DiscoveryClient send them; neither
    // exposes its constants, so they are followed here rather than shared.
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // A list of twenty names and years with a short title is a few hundred tokens; this leaves room
    // without inviting an essay.
    private const int MaxTokens = 1_200;

    // A reply naming far more than was asked for is read only this far past the cap: the rest would
    // be dropped by the plan anyway, and some slack covers names the catalogue will not resolve.
    private const int CandidateSlack = 2;

    private const string AnswerShape =
        "Respond with ONLY a JSON object: {\"title\":string,\"titles\":[{\"name\":string,\"year\":number,\"kind\":string}]}. " +
        "name = the title exactly as it is released; year = the year a film opened or a show first aired; " +
        "kind = \"film\" or \"show\". Name only real, released films and series; never invent one. No prose outside the JSON.";

    private const string MoodInstruction =
        "You suggest films and TV series for a household's evening from a MOOD they typed. You are given the MOOD, " +
        "the streaming SERVICES the household has, and possibly a few TASTE tags the viewer likes. Prefer titles " +
        "available on those services. Fit the mood first and the taste second. If AUDIENCE says children, suggest " +
        "only titles made for or suitable for children. Give at most MAX TITLES, best fit first. title = an empty string. " +
        AnswerShape;

    private const string PlaylistInstruction =
        "You build a short playlist of films and TV series for a household. You are given a SEED — either a PROMPT " +
        "they typed or SEED TITLES they want more like — the streaming SERVICES the household has, and possibly a few " +
        "TASTE tags the viewer likes. Prefer titles available on those services. Never include a seed title itself. " +
        "If AUDIENCE says children, suggest only titles made for or suitable for children. Give at most MAX TITLES, " +
        "in a good order to watch them. title = a short playlist name, a few words. " +
        AnswerShape;

    private readonly HttpClient httpClient;
    private readonly IOptions<VoiceOptions> optionsAccessor;
    private readonly ICredentialHealthSink? healthSink;

    public MoodClient(HttpClient httpClient, IOptions<VoiceOptions> options, ICredentialHealthSink? healthSink = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        this.httpClient = httpClient;
        optionsAccessor = options;
        this.healthSink = healthSink;
    }

    /// <summary>A mood search is typed and waited on, often several times an evening, so it runs on the fast tier.</summary>
    public Task<MoodAnswer> AskMoodAsync(MoodQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userContent =
            $"MOOD: {query.Mood}\n" +
            Household(query.Context) +
            $"MAX TITLES: {query.MaxTitles.ToString(CultureInfo.InvariantCulture)}";
        return AskAsync(MoodInstruction, userContent, optionsAccessor.Value.FastModel, query.MaxTitles, cancellationToken);
    }

    /// <summary>A made playlist is one considered choice the viewer keeps, so it runs on the strong tier.</summary>
    public Task<MoodAnswer> AskPlaylistAsync(PlaylistQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var seed = query.Seed.Kind == SeedKind.Prompt
            ? $"PROMPT: {query.Seed.Prompt}\n"
            : "SEED TITLES (JSON):\n" + JsonSerializer.Serialize(query.Seed.Titles.Select(title => new
            {
                name = title.Name,
                year = title.Year,
                kind = KindWord(title.Kind),
                genres = title.Genres,
            })) + "\n";
        var userContent =
            seed +
            Household(query.Context) +
            $"MAX TITLES: {query.MaxTitles.ToString(CultureInfo.InvariantCulture)}";
        return AskAsync(PlaylistInstruction, userContent, optionsAccessor.Value.Model, query.MaxTitles, cancellationToken);
    }

    private static string Household(DiscoverContext context) =>
        $"SERVICES: {JsonSerializer.Serialize(context.ServiceNames)}\n" +
        $"TASTE: {JsonSerializer.Serialize(context.TasteTags)}\n" +
        $"AUDIENCE: {(context.ForChildren ? "children" : "adults")}\n";

    private async Task<MoodAnswer> AskAsync(
        string instruction, string userContent, string model, int maxTitles, CancellationToken cancellationToken)
    {
        var apiKey = optionsAccessor.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return MoodAnswer.NoIdea(MoodOutcome.NoKey, "No Claude key is set, so nothing was asked.");
        }

        var payload = new
        {
            model,
            max_tokens = MaxTokens,
            system = instruction,
            messages = new[] { new { role = "user", content = userContent } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", apiKey.Trim());
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        int status;
        string body;
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return MoodAnswer.NoIdea(MoodOutcome.Unavailable, $"The Messages API could not be reached: {exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not the caller cancelling: the search simply has no answer this time.
            return MoodAnswer.NoIdea(MoodOutcome.Unavailable, $"The Messages API timed out: {exception.Message}");
        }

        if (status is < 200 or >= 300)
        {
            if (AuthFailure.IsHttpCredentialRejection(status, body))
            {
                healthSink?.MarkFailing(CredentialId, $"HTTP {status.ToString(CultureInfo.InvariantCulture)}");
                return MoodAnswer.NoIdea(MoodOutcome.Rejected, $"The Messages API refused the key: HTTP {status.ToString(CultureInfo.InvariantCulture)}.");
            }

            return MoodAnswer.NoIdea(MoodOutcome.Unavailable, $"The Messages API answered HTTP {status.ToString(CultureInfo.InvariantCulture)}: {Excerpt(body)}");
        }

        healthSink?.MarkOk(CredentialId);
        return Read(body, maxTitles * CandidateSlack);
    }

    /// <summary>Reads a Messages response into an answer; anything not the shape asked for is "no idea".</summary>
    private static MoodAnswer Read(string body, int maxCandidates)
    {
        string text;
        try
        {
            text = ExtractText(body);
        }
        catch (JsonException exception)
        {
            return MoodAnswer.NoIdea(MoodOutcome.Malformed, $"The reply was not a Messages response: {exception.Message}");
        }

        var json = ExtractObject(text);
        if (json is null)
        {
            return MoodAnswer.NoIdea(MoodOutcome.Malformed, "The reply held no JSON object.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("titles", out var titles)
                || titles.ValueKind != JsonValueKind.Array)
            {
                return MoodAnswer.NoIdea(MoodOutcome.Malformed, "The reply's JSON had no \"titles\" list.");
            }

            var candidates = new List<MoodCandidate>();
            foreach (var item in titles.EnumerateArray())
            {
                if (candidates.Count >= maxCandidates)
                {
                    break;
                }

                var candidate = Candidate(item);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            var title = root.TryGetProperty("title", out var named) && named.ValueKind == JsonValueKind.String
                ? named.GetString()
                : null;
            return MoodAnswer.Answered(title, candidates);
        }
        catch (JsonException exception)
        {
            return MoodAnswer.NoIdea(MoodOutcome.Malformed, $"The reply's JSON did not parse: {exception.Message}");
        }
    }

    /// <summary>One named title; a blank or missing name is skipped, an unreadable year or kind is left unknown.</summary>
    private static MoodCandidate? Candidate(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var name = nameElement.GetString()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        int? year = null;
        if (item.TryGetProperty("year", out var yearElement))
        {
            if (yearElement.ValueKind == JsonValueKind.Number && yearElement.TryGetInt32(out var number))
            {
                year = number;
            }
            else if (yearElement.ValueKind == JsonValueKind.String
                && int.TryParse(yearElement.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                year = parsed;
            }
        }

        TitleKind? kind = null;
        if (item.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String)
        {
            kind = kindElement.GetString()?.Trim().ToLowerInvariant() switch
            {
                "film" or "movie" => TitleKind.Film,
                "show" or "series" or "tv" => TitleKind.Show,
                _ => null,
            };
        }

        return new MoodCandidate(name, year, kind);
    }

    private static string KindWord(TitleKind kind) => kind == TitleKind.Film ? "film" : "show";

    // Concatenates the text blocks of a non-streaming Messages response, as VoiceClient does.
    private static string ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var type) && type.GetString() == "text"
                && block.TryGetProperty("text", out var chunk) && chunk.ValueKind == JsonValueKind.String)
            {
                text.Append(chunk.GetString());
            }
        }

        return text.ToString();
    }

    // The first balanced {...} in a reply, so a model that wraps its JSON in prose still parses.
    private static string? ExtractObject(string reply)
    {
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < reply.Length; i++)
        {
            var c = reply[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return reply[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private static string Excerpt(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}
