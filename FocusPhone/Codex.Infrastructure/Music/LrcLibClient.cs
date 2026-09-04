using System.Text.Json;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// Where the words come from. An interface because lyrics are the one thing in Score
/// that is genuinely independent of the music provider.
/// </summary>
public interface ILyricsSource
{
    Task<Lyrics> FetchAsync(
        string artist, string title, string? album, TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// Lyrics from LRCLIB — free, keyless, and about three million time-synced files.
///
/// Chosen because it is <b>service-independent</b>. Lyrics from a streaming provider
/// would only ever cover the streamed half of the library, which is exactly backwards:
/// the owner's own files are the half nothing else can answer for. This works for both
/// halves and does not wait on a subscription, an account, or a key.
///
/// The duration is part of the question, not decoration — it is what stops the service
/// returning a different recording of the same song, which for a library full of live
/// versions and acoustic cuts is most of the value.
///
/// Every failure is empty. Most tracks simply have no lyrics on file, and that is the
/// ordinary case rather than an error: the panel says so and nothing else changes.
/// </summary>
public sealed class LrcLibClient : ILyricsSource
{
    private const string GetUrl = "https://lrclib.net/api/get";

    // The service asks callers to identify themselves. Being a good guest of a free,
    // keyless service is the whole reason it stays usable.
    private const string UserAgent = "Score/1.0 (https://github.com/storyworthtelling)";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient httpClient;

    public LrcLibClient(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<Lyrics> FetchAsync(
        string artist, string title, string? album, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return Lyrics.None;
        }

        var url =
            $"{GetUrl}?artist_name={Uri.EscapeDataString(artist)}" +
            $"&track_name={Uri.EscapeDataString(title)}" +
            $"&album_name={Uri.EscapeDataString(album ?? string.Empty)}" +
            $"&duration={(int)duration.TotalSeconds}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Lyrics.None; // 404 is "nobody has typed these up", which is common
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var found = JsonSerializer.Deserialize<LyricsReply>(json, SerializerOptions);
            return LrcParser.Parse(found?.SyncedLyrics, found?.PlainLyrics);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return Lyrics.None;
        }
    }

    private sealed record LyricsReply(string? SyncedLyrics, string? PlainLyrics);
}
