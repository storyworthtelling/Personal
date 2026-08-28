using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// The Claude-backed Discovery engine. Reuses the shared <see cref="VoiceOptions"/> key + model tiers, like
/// the taste client. It's handed the Taste Profile and a digest of the catalog (id + title/artist/genre) and
/// returns the ids it chose; a garbled or empty reply yields <see cref="DiscoverySelection.Empty"/> so the
/// caller simply shows nothing. Raw-HTTP, non-streaming — the reply is a short id list.
/// </summary>
public sealed class DiscoveryClient : IDiscoveryClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 1_200;

    private const string PlaylistInstruction =
        "You are a music curator picking a playlist from ONE listener's own library. You are given their " +
        "TASTE PROFILE, a CATALOG (each track: id, title, artist, genre), and a REQUEST (a mood/genre/idea). " +
        "Choose tracks from the catalog that fit the request and their taste, honoring what the profile says " +
        "they avoid. Use ONLY ids present in the catalog. Respond with ONLY a JSON object: " +
        "{\"title\":string,\"trackIds\":[string]}. title = a short playlist name. trackIds = the chosen ids " +
        "in play order, at most the requested count. No prose outside the JSON.";

    private const string FridayInstruction =
        "You are assembling a quiet weekly \"New for you\" set from ONE listener's own library, to resurface " +
        "things that fit their taste. You are given their TASTE PROFILE and a CATALOG (each track: id, title, " +
        "artist, genre). Pick a small set that suits their taste, honoring what they avoid, favoring variety " +
        "over their most-played. Use ONLY ids present in the catalog. Respond with ONLY a JSON object: " +
        "{\"title\":string,\"trackIds\":[string]}. No prose outside the JSON.";

    private readonly HttpClient httpClient;
    private readonly IOptions<VoiceOptions> optionsAccessor;

    public DiscoveryClient(HttpClient httpClient, IOptions<VoiceOptions> options)
    {
        this.httpClient = httpClient;
        optionsAccessor = options;
    }

    public Task<DiscoverySelection> GeneratePlaylistAsync(
        TasteProfile profile, IReadOnlyList<Track> catalog, PlaylistRequest request, CancellationToken cancellationToken)
    {
        var userContent =
            $"TASTE PROFILE (JSON):\n{ProfileJson(profile)}\n\n" +
            $"CATALOG (JSON):\n{CatalogJson(catalog)}\n\n" +
            $"REQUEST: {request.Seed}\nMAX TRACKS: {request.MaxTracks}";
        return Choose(PlaylistInstruction, userContent, cancellationToken);
    }

    public Task<DiscoverySelection> FridayPicksAsync(
        TasteProfile profile, IReadOnlyList<Track> catalog, int maxTracks,
        string? note, CancellationToken cancellationToken)
    {
        var userContent =
            $"TASTE PROFILE (JSON):\n{ProfileJson(profile)}\n\n" +
            $"CATALOG (JSON):\n{CatalogJson(catalog)}\n\nMAX TRACKS: {maxTracks}" +
            (string.IsNullOrWhiteSpace(note) ? string.Empty : $"\n\nTHE SHELF IS SHORT OF: {note}");
        return Choose(FridayInstruction, userContent, cancellationToken);
    }

    private async Task<DiscoverySelection> Choose(string instruction, string userContent, CancellationToken cancellationToken)
    {
        var reply = await SendMessage(instruction, userContent, cancellationToken);
        var json = ExtractObject(reply);
        if (json is null)
        {
            return DiscoverySelection.Empty;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<SelectionDto>(json, CaseInsensitive);
            if (dto is null)
            {
                return DiscoverySelection.Empty;
            }

            return new DiscoverySelection(dto.Title?.Trim() ?? string.Empty, dto.TrackIds ?? []);
        }
        catch (JsonException)
        {
            return DiscoverySelection.Empty;
        }
    }

    private static string ProfileJson(TasteProfile profile) => JsonSerializer.Serialize(new
    {
        summary = profile.Summary,
        loves = profile.Loves,
        leans = profile.Leans,
        avoids = profile.Avoids,
        topGenres = profile.TopGenres,
        topArtists = profile.TopArtists,
        listensBy = profile.ListensBy,
        explicitAdvisory = profile.ExplicitAdvisory
    });

    // The catalog can be large, so the prompt carries a bounded sample of it — taken
    // across artists by CatalogDigest rather than off the top, because the head of an
    // artist-sorted catalog is the letter A and nothing else.
    private static string CatalogJson(IReadOnlyList<Track> catalog) => JsonSerializer.Serialize(
        CatalogDigest.Sample(catalog).Select(track => new
        {
            id = track.Id,
            title = track.DisplayTitle,
            artist = track.DisplayArtist,
            genre = track.DisplayGenre
        }));

    private async Task<string> SendMessage(string instruction, string userText, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = optionsAccessor.Value.Model,
            max_tokens = MaxTokens,
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
                $"Anthropic discovery request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}");
        }

        return ExtractText(body);
    }

    private static string ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
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

    private sealed class SelectionDto
    {
        public string? Title { get; set; }
        public List<string>? TrackIds { get; set; }
    }
}
