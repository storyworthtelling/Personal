using System.Net;
using Codex.Domain.Apps;

namespace Codex.Infrastructure.Apps;

/// <summary>
/// Fetches an app's US Google Play listing and reads its content rating. A network failure throws
/// (the caller records it per app); a status the page answers with is turned into an outcome.
/// </summary>
public class PlayListingClient
{
    private readonly HttpClient http;

    public PlayListingClient(HttpClient http)
    {
        this.http = http;
    }

    public static string ListingUrl(string package) =>
        $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(package)}&hl=en_US&gl=US";

    public async Task<PlayLookup> LookupAsync(string package)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ListingUrl(package));
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 15) Flyleaf");

        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PlayLookup(RatingLookupOutcome.NotListed, ContentRating.Unknown);
        }
        if (!response.IsSuccessStatusCode)
        {
            return new PlayLookup(RatingLookupOutcome.Failed, ContentRating.Unknown);
        }

        var rating = PlayRating.ReadListing(await response.Content.ReadAsStringAsync());
        return rating is ContentRating.Unknown
            ? new PlayLookup(RatingLookupOutcome.Unreadable, ContentRating.Unknown)
            : new PlayLookup(RatingLookupOutcome.Rated, rating);
    }
}
