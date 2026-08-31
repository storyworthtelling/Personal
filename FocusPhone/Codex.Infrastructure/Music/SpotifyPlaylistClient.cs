using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>A playlist as some other service listed it: a name and the tracks, by name.</summary>
public sealed record SharedPlaylist(string Title, IReadOnlyList<ImportedTrack> Tracks)
{
    public static SharedPlaylist Empty { get; } = new(string.Empty, []);

    public bool IsEmpty => Tracks.Count == 0;
}

/// <summary>
/// Where a shared playlist's track list comes from. An interface because the source
/// is the one part of importing that is outside our control, and the resolver behind
/// it — <see cref="PlaylistImport"/> — does not care which service named the songs.
/// </summary>
public interface ISharedPlaylistSource
{
    Task<SharedPlaylist> FetchAsync(string playlistId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads a public Spotify playlist from its <b>embed page</b>.
///
/// This is deliberately the unsanctioned route, and the reason is worth writing down:
/// Spotify's API cannot serve this. Development Mode requires the app's owner to hold
/// an active Premium subscription, and Client Credentials no longer serves playlist
/// metadata — so the moment the household's Spotify subscription ends, the supported
/// path ends with it. The embed page needs no account at all.
///
/// What that costs: it is scraping. It breaks whenever Spotify changes the page, and
/// it sits outside the API terms. So it is built to fail quietly — a page that cannot
/// be read comes back <see cref="SharedPlaylist.Empty"/> and the owner is told the
/// import found nothing, rather than an exception reaching a share they are watching.
/// Nothing else in Score depends on it.
///
/// Only the title and the credited artist are taken. That is all the resolver needs
/// to find the song in a library that is already there.
/// </summary>
public sealed class SpotifyPlaylistClient : ISharedPlaylistSource
{
    private const string EmbedUrl = "https://open.spotify.com/embed/playlist/";

    // The page is served to browsers. Without a browser-shaped agent the response is
    // not the page we parse, so this is load-bearing rather than politeness.
    private const string UserAgent =
        "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";

    private readonly HttpClient httpClient;

    public SpotifyPlaylistClient(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<SharedPlaylist> FetchAsync(string playlistId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, EmbedUrl + playlistId);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return SharedPlaylist.Empty;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return SpotifyEmbedPage.Read(html);
    }
}

/// <summary>
/// Pulls a playlist out of an embed page's HTML. Pure, so the fragile half of the
/// import is the half that is covered by tests.
///
/// It <b>walks</b> the embedded JSON looking for a list of things that have a name
/// and someone credited, rather than following a path into it. The path is Spotify's
/// to change and ours to discover the hard way; the shape of a track — a title and an
/// artist — is what the page is actually for, and it survives the furniture around it
/// moving. A list of names with nobody credited is not a track list, because taking
/// one would invent songs the setlist never named.
/// </summary>
public static partial class SpotifyEmbedPage
{
    public static SharedPlaylist Read(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return SharedPlaylist.Empty;
        }

        var best = new List<ImportedTrack>();
        string? entityTitle = null;
        foreach (Match script in JsonScript().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                Search(document.RootElement, best, ref entityTitle);
            }
            catch (JsonException)
            {
                // A blob that is not JSON after all, or is truncated. Another script
                // on the page may still carry the list.
            }
        }

        if (best.Count == 0)
        {
            return SharedPlaylist.Empty;
        }

        var title = entityTitle ?? ReadTitleFromHtml(html);
        return new SharedPlaylist(title, best);
    }

    // Depth-first, keeping the longest track list found: an embed page carries
    // recommendation shelves beside the playlist, and the playlist is the long one.
    //
    // The check happens one level up, at the object holding the array, because that
    // object is also where the playlist's own name lives — as a "name" or "title"
    // sibling of the track list it names. The page itself no longer says the name
    // anywhere else: Spotify's embed is client-hydrated now, with no og:title meta
    // and an empty <title>, so the JSON is the only reliable source left.
    private static void Search(JsonElement element, List<ImportedTrack> best, ref string? title)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    var tracks = AsTrackList(property.Value);
                    if (tracks.Count > best.Count)
                    {
                        best.Clear();
                        best.AddRange(tracks);
                        title = Text(element, "name") ?? Text(element, "title");
                    }
                }

                Search(property.Value, best, ref title);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Search(item, best, ref title);
            }
        }
    }

    // Every element has to read as a track. A list where only some do is a mixed
    // shelf — shows, episodes, artists — and taking the half that parses would be a
    // silently wrong setlist.
    private static List<ImportedTrack> AsTrackList(JsonElement array)
    {
        var tracks = new List<ImportedTrack>();
        foreach (var item in array.EnumerateArray())
        {
            if (AsTrack(item) is not { } track)
            {
                return [];
            }

            tracks.Add(track);
        }

        return tracks;
    }

    private static ImportedTrack? AsTrack(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // The Web API wraps each entry in a playlist item; the embed page does not.
        if (item.TryGetProperty("track", out var inner) && inner.ValueKind == JsonValueKind.Object)
        {
            return AsTrack(inner);
        }

        var title = Text(item, "title") ?? Text(item, "name");
        var artist = Text(item, "subtitle") ?? Text(item, "artist") ?? Credited(item);
        return title is null || artist is null ? null : new ImportedTrack(title, artist);
    }

    // Every performer the entry credits, in the order it credits them. The resolver
    // matches a tag crediting one of them against the whole list.
    private static string? Credited(JsonElement item)
    {
        if (!item.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = artists.EnumerateArray()
            .Select(artist => Text(artist, "name"))
            .Where(name => name is not null)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    // Fallback for when the JSON carried no name for the entity that owned the track
    // list. Real embed pages don't populate either of these today, but a page that
    // still does should not be made to pay for one that doesn't.
    private static string ReadTitleFromHtml(string html)
    {
        var open = OpenGraphTitle().Match(html);
        var title = open.Success ? open.Groups[1].Value : PageTitle().Match(html) is { Success: true } m
            ? m.Groups[1].Value
            : string.Empty;
        return System.Net.WebUtility.HtmlDecode(title).Trim();
    }

    [GeneratedRegex("""<script[^>]*type="application/json"[^>]*>(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex JsonScript();

    [GeneratedRegex("""<meta[^>]*property="og:title"[^>]*content="([^"]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex OpenGraphTitle();

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PageTitle();
}
