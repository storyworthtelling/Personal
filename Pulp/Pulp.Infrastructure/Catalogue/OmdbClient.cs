using Pulp.Domain.Catalogue;
using Pulp.Domain.Ratings;
using Storyworthtelling.Voice.Health;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>
/// The rating source: OMDb, read for the rating code and nothing else (D207 — the chip only, never
/// reasons; guarded by <c>OmdbReadsTheCodeOnlyTests</c>). Per https://www.omdbapi.com/ the key is the
/// <c>apikey</c> query parameter and a title is asked for by IMDb id with <c>i=</c>; the answer's
/// <c>Rated</c> is the code, and <c>Response</c> is "False" with an <c>Error</c> when there is none.
/// An invalid key is HTTP 401 "Invalid API key!"; the daily allowance running out also comes back as
/// 401, "Request limit reached!", which is throttling rather than a bad key.
/// </summary>
public sealed class OmdbClient
{
    private static readonly Dictionary<string, string> NoHeaders = [];

    private readonly IPulpKeySource keys;
    private readonly ProviderHttp http;

    public OmdbClient(HttpClient httpClient, IPulpKeySource keys, ILookupCounter? counter = null, ICredentialHealthSink? healthSink = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        this.keys = keys;
        http = new ProviderHttp(httpClient, LookupSource.Omdb, PulpCredentialIds.Omdb, counter, healthSink,
            response => response.Body.Contains("limit reached", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The title's rating, read into Pulp's two systems; anything else, or no answer, is unrated (D128).</summary>
    public async Task<ContentRating> GetRatingAsync(string imdbId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);
        var key = http.RequireKey(keys.Keys.OmdbKey, "OMDb");
        var what = "a rating lookup";
        var response = await http.GetAsync(
            new Uri("https://www.omdbapi.com/?i=" + Uri.EscapeDataString(imdbId.Trim()) + "&apikey=" + Uri.EscapeDataString(key)),
            NoHeaders, what, cancellationToken);

        using var document = http.Parse(response, what);
        var root = document.RootElement;
        if (!string.Equals(root.Text("Response"), "True", StringComparison.OrdinalIgnoreCase))
        {
            // "Incorrect IMDb ID." and the like: OMDb has no rating for it, which is unrated, not a failure.
            return ContentRating.Unrated;
        }

        return ContentRating.Parse(root.Text("Rated"));
    }
}
