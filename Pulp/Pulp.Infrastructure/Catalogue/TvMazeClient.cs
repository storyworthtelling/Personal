using System.Globalization;
using System.Net;
using System.Text.Json;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Library;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>
/// The episode source: TVmaze, keyless (finding §1.1), for air dates (D21, D146) and whether a show
/// has ended. Shapes from https://www.tvmaze.com/api — <c>/lookup/shows?imdb=</c> answers with a
/// 301 to the show (404 when unknown), and <c>/shows/{id}/episodes</c> lists every episode in airing
/// order with <c>season</c>, <c>number</c> (null for a special), <c>airdate</c> and <c>airstamp</c>.
/// No credential, so no health is reported; TVmaze asks for an identifying User-Agent.
/// </summary>
public sealed class TvMazeClient
{
    private const string Root = "https://api.tvmaze.com";

    private static readonly Dictionary<string, string> Identify = new() { ["User-Agent"] = "Pulp (storyworthtelling household guide)" };

    private readonly ProviderHttp http;

    public TvMazeClient(HttpClient httpClient, ILookupCounter? counter = null) =>
        http = new ProviderHttp(httpClient, LookupSource.TvMaze, credentialId: null, counter, healthSink: null);

    /// <summary>The show's air dates by its IMDb id; null when TVmaze does not know the show.</summary>
    public async Task<ShowAirDates?> GetAirDatesByImdbAsync(string imdbId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);
        var what = "a show lookup";
        var lookup = await http.GetAsync(
            new Uri(Root + "/lookup/shows?imdb=" + Uri.EscapeDataString(imdbId.Trim())), Identify, what, cancellationToken,
            HttpStatusCode.NotFound, HttpStatusCode.MovedPermanently, HttpStatusCode.Found);
        if (lookup.Status == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (lookup.Status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found)
        {
            // A client that does not follow redirects sees the documented 301 itself; follow it here.
            var location = lookup.Location ?? throw http.Malformed(what, "a redirect with no location");
            lookup = await http.GetAsync(location.IsAbsoluteUri ? location : new Uri(new Uri(Root), location), Identify, what, cancellationToken);
        }

        int showId;
        bool ended;
        using (var show = http.Parse(lookup, what))
        {
            showId = show.RootElement.Number("id") ?? throw http.Malformed(what, "a show without an id");
            ended = string.Equals(show.RootElement.Text("status"), "Ended", StringComparison.OrdinalIgnoreCase);
        }

        what = "an episode list";
        var listing = await http.GetAsync(
            new Uri(Root + "/shows/" + showId.ToString(CultureInfo.InvariantCulture) + "/episodes"), Identify, what, cancellationToken);
        using var episodes = http.Parse(listing, what);
        if (episodes.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "expected an array of episodes");
        }

        return new ShowAirDates(ended, episodes.RootElement.EnumerateArray().Select(Aired).OfType<AiredEpisode>().ToList());
    }

    /// <summary>An episode with a season and a number; a special has no number and no place in the season.</summary>
    private static AiredEpisode? Aired(JsonElement episode)
    {
        if (episode.Number("season") is not { } season || episode.Number("number") is not { } number || season < 1 || number < 1)
        {
            return null;
        }

        DateOnly? date = DateOnly.TryParseExact(episode.Text("airdate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
        DateTimeOffset? stamp = DateTimeOffset.TryParse(episode.Text("airstamp"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : null;
        return new AiredEpisode(new EpisodeRef(season, number), episode.Text("name") ?? string.Empty, date, stamp);
    }
}
