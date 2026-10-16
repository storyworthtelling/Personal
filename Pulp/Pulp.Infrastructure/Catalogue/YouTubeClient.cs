using System.Globalization;
using Pulp.Domain.Catalogue;
using Storyworthtelling.Voice.Health;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>
/// The trailer source: the YouTube Data API's search.list
/// (https://developers.google.com/youtube/v3/docs/search/list), which replaced TMDB's video keys
/// (D203). The key is the <c>key</c> query parameter; a result's id is <c>items[].id.videoId</c>.
/// One embeddable video is asked for, since the trailer plays inside Pulp's player. An invalid key is
/// HTTP 400 with <c>API_KEY_INVALID</c>; an exhausted daily quota is HTTP 403 <c>quotaExceeded</c>,
/// which is throttling rather than a bad key and so does not reveal the key's field.
/// </summary>
public sealed class YouTubeClient
{
    private static readonly Dictionary<string, string> NoHeaders = [];

    private readonly IPulpKeySource keys;
    private readonly ProviderHttp http;

    public YouTubeClient(HttpClient httpClient, IPulpKeySource keys, ILookupCounter? counter = null, ICredentialHealthSink? healthSink = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        this.keys = keys;
        http = new ProviderHttp(httpClient, LookupSource.YouTube, PulpCredentialIds.YouTube, counter, healthSink,
            response => response.Body.Contains("quotaExceeded", StringComparison.Ordinal)
                || response.Body.Contains("rateLimitExceeded", StringComparison.Ordinal)
                || response.Body.Contains("dailyLimitExceeded", StringComparison.Ordinal));
    }

    /// <summary>The video id of the title's trailer, or null when the search finds none.</summary>
    public async Task<string?> FindTrailerAsync(string name, int? year, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = http.RequireKey(keys.Keys.YouTubeKey, "YouTube");
        var phrase = name.Trim() + (year is null ? string.Empty : " " + year.Value.ToString(CultureInfo.InvariantCulture)) + " official trailer";
        var what = "a trailer search";
        var response = await http.GetAsync(
            new Uri("https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=1&videoEmbeddable=true&q="
                + Uri.EscapeDataString(phrase) + "&key=" + Uri.EscapeDataString(key)),
            NoHeaders, what, cancellationToken);

        using var document = http.Parse(response, what);
        return document.RootElement.Child("items").Items()
            .Select(item => item.Child("id").Text("videoId"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
    }

    /// <summary>
    /// The video id of one embeddable video inside a channel, published in a window (a game's highlights, D313).
    /// The title and thumbnail in the response are never read: the caller wants an id to play, and the text is feed
    /// text that can say how a game went.
    /// </summary>
    public async Task<string?> FindInChannelAsync(string channelId, string phrase, DateTimeOffset publishedAfter, DateTimeOffset publishedBefore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        var key = http.RequireKey(keys.Keys.YouTubeKey, "YouTube");
        var what = "a highlights search";
        var response = await http.GetAsync(
            new Uri("https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=1&videoEmbeddable=true"
                + "&channelId=" + Uri.EscapeDataString(channelId)
                + "&publishedAfter=" + Uri.EscapeDataString(Stamp(publishedAfter))
                + "&publishedBefore=" + Uri.EscapeDataString(Stamp(publishedBefore))
                + "&q=" + Uri.EscapeDataString(phrase) + "&key=" + Uri.EscapeDataString(key)),
            NoHeaders, what, cancellationToken);

        using var document = http.Parse(response, what);
        return document.RootElement.Child("items").Items()
            .Select(item => item.Child("id").Text("videoId"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
