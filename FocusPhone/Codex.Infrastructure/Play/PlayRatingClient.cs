using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Domain.Apps;

namespace Codex.Infrastructure.Play;

/// <summary>
/// Reads an app's maturity rating from the JSON-LD metadata of its public Play
/// Store listing. There is no on-device API for this, so the source is the web
/// page — inherently brittle, which is why every undeterminable case returns
/// Unknown and the categorizer quarantines on Unknown (fail-closed: scraping
/// breakage can only ever over-block, never under-block).
/// </summary>
public class PlayRatingClient
{
    private static readonly Regex JsonLdScript = new(
        "<script type=\"application/ld\\+json\"[^>]*>(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly HttpClient http;

    public PlayRatingClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<ContentRating> GetContentRatingAsync(string packageName)
    {
        var url = $"https://play.google.com/store/apps/details?id={packageName}&hl=en";
        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return ContentRating.Unknown;
        }

        var match = JsonLdScript.Match(await response.Content.ReadAsStringAsync());
        if (!match.Success)
        {
            return ContentRating.Unknown;
        }

        try
        {
            using var json = JsonDocument.Parse(match.Groups[1].Value);
            if (!json.RootElement.TryGetProperty("contentRating", out var rating))
            {
                return ContentRating.Unknown;
            }
            return MapLabel(rating.GetString());
        }
        catch (JsonException)
        {
            // Malformed page metadata means the rating cannot be determined;
            // Unknown quarantines the app upstream rather than letting it through.
            return ContentRating.Unknown;
        }
    }

    private static ContentRating MapLabel(string? label)
    {
        if (label is null)
        {
            return ContentRating.Unknown;
        }
        if (label.Contains("everyone", StringComparison.OrdinalIgnoreCase))
        {
            return ContentRating.Everyone;
        }
        if (label.Contains("teen", StringComparison.OrdinalIgnoreCase))
        {
            return ContentRating.Teen;
        }
        if (label.Contains("mature", StringComparison.OrdinalIgnoreCase)
            || label.Contains("adults", StringComparison.OrdinalIgnoreCase))
        {
            return ContentRating.Mature;
        }
        return ContentRating.Unknown;
    }
}
