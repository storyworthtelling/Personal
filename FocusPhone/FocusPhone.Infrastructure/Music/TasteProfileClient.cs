using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// The Claude-backed Taste Profile client. Reuses the shared <see cref="VoiceOptions"/> key and model tiers
/// (the owner enters the Claude key once, in Storyboarder's settings) rather than a FocusPhone-specific key.
/// A self-contained non-streaming raw-HTTP client like the Voice library's — these calls are short (a
/// distilled profile from a compact evidence digest). Failures propagate with a descriptive message.
/// </summary>
public sealed class TasteProfileClient : ITasteProfileClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int RefineMaxTokens = 1_500;

    // How much of the ranked evidence to hand the model — the head of each list is what characterizes the
    // taste; the long tail is noise and only bloats the prompt.
    private const int TopKeysPerFacet = 12;

    private const string RefineInstruction =
        "You are a music-taste analyst distilling how one listener actually listens, from passive signals " +
        "only (completions, skips, replays, whether they choose albums vs shuffle, and time of day) — never " +
        "ratings. You are given the CURRENT PROFILE (possibly empty) and NEW EVIDENCE aggregated from their " +
        "recent listening. Update the profile, folding the evidence into what's already there rather than " +
        "replacing it. Infer taste, not a track log. Respond with ONLY a JSON object: " +
        "{\"summary\":string,\"loves\":[string],\"leans\":[string],\"avoids\":[string]," +
        "\"topGenres\":[string],\"topArtists\":[string],\"listensBy\":[string],\"explicitAdvisory\":bool}. " +
        "summary = a short paragraph on what they reach for and when. loves = strong positives. leans = " +
        "softer tendencies (e.g. \"prefers albums over shuffle\"). avoids = what reads as not-for-them, so " +
        "recommendations can steer clear. listensBy = time-of-day tendencies. explicitAdvisory = true only " +
        "if the evidence suggests they steer away from explicit content. Keep each list to at most 6 items. " +
        "No prose outside the JSON.";

    private readonly HttpClient httpClient;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    public TasteProfileClient(HttpClient httpClient, IOptions<VoiceOptions> options)
    {
        this.httpClient = httpClient;
        optionsAccessor = options;
    }

    public async Task<TasteProfile> RefineAsync(
        TasteProfile existing, TasteEvidence evidence, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;

        var existingJson = JsonSerializer.Serialize(new
        {
            summary = existing.Summary,
            loves = existing.Loves,
            leans = existing.Leans,
            avoids = existing.Avoids,
            topGenres = existing.TopGenres,
            topArtists = existing.TopArtists,
            listensBy = existing.ListensBy,
            explicitAdvisory = existing.ExplicitAdvisory
        });
        var evidenceJson = JsonSerializer.Serialize(new
        {
            genres = Digest(evidence.Genres),
            artists = Digest(evidence.Artists),
            albumArtists = Digest(evidence.AlbumArtists),
            hourHistogram = evidence.HourHistogram,
            sessionsAnalyzed = evidence.SessionsAnalyzed
        });
        var userContent = $"CURRENT PROFILE (JSON):\n{existingJson}\n\nNEW EVIDENCE (JSON):\n{evidenceJson}";

        var reply = await SendMessage(
            RefineInstruction, userContent, options.Model, RefineMaxTokens, cancellationToken);

        var json = ExtractObject(reply);
        if (json is null)
        {
            return existing; // a garbled reply must never corrupt a good profile
        }

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
        {
            return existing;
        }

        // Provenance is counted from the evidence, not trusted to the model.
        return new TasteProfile(
            dto.Summary.Trim(),
            dto.Loves ?? [],
            dto.Leans ?? [],
            dto.Avoids ?? [],
            dto.TopGenres ?? [],
            dto.TopArtists ?? [],
            dto.ListensBy ?? [],
            dto.ExplicitAdvisory,
            evidence.SessionsAnalyzed);
    }

    private static IEnumerable<object> Digest(IReadOnlyList<AffinityStat> stats) =>
        stats.Take(TopKeysPerFacet).Select(stat => new
        {
            key = stat.Key,
            sessions = stat.Sessions,
            completions = stat.Completions,
            skips = stat.Skips,
            replays = stat.Replays,
            albumPlays = stat.AlbumPlays,
            shufflePlays = stat.ShufflePlays
        });

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
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Anthropic taste request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}");
        }

        return ExtractText(body);
    }

    private static string ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                block.TryGetProperty("text", out var chunk))
            {
                text.Append(chunk.GetString());
            }
        }

        return text.ToString();
    }

    // Pulls the first balanced {...} object out of a reply so a model that wraps its JSON in prose still parses.
    private static string? ExtractObject(string reply)
    {
        var start = reply.IndexOf('{');
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
                    {
                        return reply[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private sealed class ProfileDto
    {
        public string? Summary { get; set; }
        public List<string>? Loves { get; set; }
        public List<string>? Leans { get; set; }
        public List<string>? Avoids { get; set; }
        public List<string>? TopGenres { get; set; }
        public List<string>? TopArtists { get; set; }
        public List<string>? ListensBy { get; set; }
        public bool ExplicitAdvisory { get; set; }
    }
}
