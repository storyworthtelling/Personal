using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Music;

/// <summary>What Claude came back with for a hidden track's name — never a guess, only
/// a name Claude was actually confident of, or nothing.</summary>
public sealed record HiddenTrackNameResult(bool Found, string Title = "")
{
    public static HiddenTrackNameResult Unknown { get; } = new(false);
}

public interface IHiddenTrackNamer
{
    Task<HiddenTrackNameResult> NameAsync(
        string albumTitle, string albumArtist, string hostTrackTitle, CancellationToken cancellationToken);
}

/// <summary>
/// Names a detected hidden track by asking Claude — the same shared vault key and
/// model tiers <see cref="DiscoveryClient"/> already uses. A hidden track has no tag
/// of its own to read (it was never a separate file), and there is no structured API
/// that reliably lists it either, so this is a knowledge question rather than a lookup:
/// "what is the bonus track after Track X on Album by Artist", the same question a
/// person would put to a search engine or Wikipedia.
///
/// Raw-HTTP, non-streaming, matching the repo's other Claude clients. Every failure —
/// a rejected key, a garbled reply, an honest "I don't know" — comes back
/// <see cref="HiddenTrackNameResult.Unknown"/>: this only ever adds a name, it never
/// blocks the split from taking effect.
/// </summary>
public sealed class HiddenTrackNamer(HttpClient httpClient, IOptions<VoiceOptions> options) : IHiddenTrackNamer
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 300;

    private const string Instruction =
        "You identify hidden (bonus) tracks on physical album releases: a song appended after the last " +
        "listed track on a CD or vinyl side, separated from it by a stretch of silence, and never printed on " +
        "the track listing. You are given the ALBUM, the ARTIST, and the HOST TRACK — the listed track the " +
        "hidden song is appended to. If you know, from your own knowledge, the actual title of the hidden " +
        "track on this specific album, respond with ONLY a JSON object: {\"found\":true,\"title\":string}. " +
        "If you do not know of a hidden track on this album, or are not genuinely confident of its title, " +
        "respond with ONLY {\"found\":false}. A guessed or plausible-sounding title is worse than admitting " +
        "you do not know — never invent one.";

    public async Task<HiddenTrackNameResult> NameAsync(
        string albumTitle, string albumArtist, string hostTrackTitle, CancellationToken cancellationToken)
    {
        var userContent = $"ALBUM: {albumTitle}\nARTIST: {albumArtist}\nHOST TRACK: {hostTrackTitle}";

        string reply;
        try
        {
            reply = await SendMessage(userContent, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return HiddenTrackNameResult.Unknown;
        }

        var json = ExtractObject(reply);
        if (json is null)
        {
            return HiddenTrackNameResult.Unknown;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<NameDto>(json, CaseInsensitive);
            var title = dto?.Title?.Trim() ?? string.Empty;
            return dto?.Found == true && !string.IsNullOrEmpty(title)
                ? new HiddenTrackNameResult(true, title)
                : HiddenTrackNameResult.Unknown;
        }
        catch (JsonException)
        {
            return HiddenTrackNameResult.Unknown;
        }
    }

    private async Task<string> SendMessage(string userText, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = options.Value.Model,
            max_tokens = MaxTokens,
            system = Instruction,
            messages = new[] { new { role = "user", content = userText } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.Value.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Anthropic hidden-track request failed: {(int)response.StatusCode} ({response.StatusCode}): {body}");
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

    private sealed class NameDto
    {
        public bool Found { get; set; }
        public string? Title { get; set; }
    }
}
