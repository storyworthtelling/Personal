using System.Xml;
using System.Xml.Linq;
using FocusPhone.Domain.Audio;

namespace FocusPhone.Infrastructure.Podcasts;

public record PodcastFeed(string Title, IReadOnlyList<Episode> Episodes);

/// <summary>
/// Fetches and parses an RSS podcast feed into Domain episodes. Items without
/// an audio enclosure are not playable and are skipped. The feed URL doubles
/// as the FeedId so episodes stay correlated to their subscription.
/// </summary>
public class RssFeedClient
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    private readonly HttpClient http;

    public RssFeedClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<PodcastFeed> FetchAsync(Uri feedUrl)
    {
        var response = await http.GetAsync(feedUrl);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Fetching podcast feed {feedUrl} failed with {(int)response.StatusCode} {response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync();
        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch (XmlException e)
        {
            // A moved or misconfigured feed often answers 200 with an HTML error
            // or redirect page rather than RSS. Surface it as invalid feed data —
            // the same family the refresh loop already handles — instead of
            // leaking a raw XmlException that escapes and crashes the app.
            throw new InvalidDataException($"Feed {feedUrl} is not valid XML: {e.Message}", e);
        }

        var channel = document.Root?.Element("channel")
            ?? throw new InvalidDataException($"Feed {feedUrl} is not valid RSS: no <channel> element.");

        var episodes = channel.Elements("item")
            .Select(item => ParseItem(item, feedUrl))
            .Where(episode => episode is not null)
            .Select(episode => episode!)
            .ToList();

        return new PodcastFeed(channel.Element("title")?.Value ?? feedUrl.ToString(), episodes);
    }

    private static Episode? ParseItem(XElement item, Uri feedUrl)
    {
        var audioUrl = item.Element("enclosure")?.Attribute("url")?.Value;
        if (audioUrl is null)
        {
            return null;
        }

        var id = item.Element("guid")?.Value ?? audioUrl;
        var title = item.Element("title")?.Value ?? id;
        var publishedUtc = DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.MinValue;

        return new Episode(id, feedUrl.ToString(), title, publishedUtc,
            ParseDuration(item.Element(Itunes + "duration")?.Value), audioUrl);
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(':');
        return parts.Length switch
        {
            1 when int.TryParse(parts[0], out var seconds) => TimeSpan.FromSeconds(seconds),
            2 when int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s) =>
                new TimeSpan(0, m, s),
            3 when int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m)
                && int.TryParse(parts[2], out var s) => new TimeSpan(h, m, s),
            _ => null,
        };
    }
}
