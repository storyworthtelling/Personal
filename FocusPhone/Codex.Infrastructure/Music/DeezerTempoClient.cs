using System.Text.Json;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// Where a tempo comes from when the file's tags do not carry one. An interface
/// because tempo, like lyrics, is independent of whoever provides the music.
/// </summary>
public interface ITempoSource
{
    /// <summary>The track's tempo in BPM, or 0 for unknown.</summary>
    Task<int> LookupAsync(string artist, string title, CancellationToken cancellationToken);
}

/// <summary>
/// Tempo from Deezer's public catalogue — keyless, no account, and it answers for
/// music the owner owns rather than only for music somebody streams.
///
/// It is here because most consumer files carry no BPM tag at all, which would leave
/// the workout pool empty however good the matching is. The alternative is analysing
/// the audio on the device, which is real work and a whole decode pipeline; a lookup
/// costs two HTTP calls and no battery.
///
/// Two calls, because the search names the song and the tempo lives on the track
/// itself — worth saying out loud, since it doubles the cost of a backfill over a
/// library.
///
/// A result is <b>refused unless it is the same song</b>. A wrong tempo is worse than
/// none: a pool built on it plays music that does not keep step, and nothing on the
/// screen would say why.
/// </summary>
public sealed class DeezerTempoClient : ITempoSource
{
    private const string SearchUrl = "https://api.deezer.com/search?q=";
    private const string TrackUrl = "https://api.deezer.com/track/";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient httpClient;

    public DeezerTempoClient(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<int> LookupAsync(string artist, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return 0;
        }

        try
        {
            var query = Uri.EscapeDataString($"artist:\"{artist}\" track:\"{title}\"");
            var results = await Read<SearchReply>(SearchUrl + query, cancellationToken);

            var match = (results?.Data ?? []).FirstOrDefault(found =>
                TrackIdentity.SameSong(found.Title, found.Artist?.Name, title, artist));
            if (match is null)
            {
                return 0;
            }

            var track = await Read<TrackReply>(TrackUrl + match.Id, cancellationToken);

            // Deezer reports 0 for a track nobody has measured, which is the same thing
            // Score means by it: unknown, not slow.
            // Away from zero, not to even: 142.5 is 143 to anyone reading it.
            return track?.Bpm is > 0 ? (int)Math.Round(track.Bpm, MidpointRounding.AwayFromZero) : 0;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return 0;
        }
    }

    private async Task<T?> Read<T>(string url, CancellationToken cancellationToken)
    {
        var json = await httpClient.GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    private sealed record SearchReply(List<SearchResult>? Data);

    private sealed record SearchResult(long Id, string? Title, ArtistReply? Artist);

    private sealed record ArtistReply(string? Name);

    private sealed record TrackReply(double Bpm);
}
