using System.Xml;
using System.Xml.Linq;
using Codex.Domain.Audio;

namespace Codex.Infrastructure.Podcasts;

public record PodcastFeed(string Title, IReadOnlyList<Episode> Episodes);

/// <summary>
/// Fetches and parses a podcast feed into Domain episodes. Handles RSS 2.0 and
/// Atom, and looks for the audio in every place feeds actually put it — a bare
/// &lt;enclosure&gt;, one of several enclosures, Media RSS, or an Atom enclosure
/// link. Only an item with no audio anywhere is skipped, since it is not playable.
///
/// The narrow "&lt;enclosure url&gt; or nothing" rule this replaced is what made
/// shows appear on the shelf with no playable episodes in them.
///
/// The feed URL doubles as the FeedId so episodes stay correlated to their
/// subscription.
/// </summary>
public class RssFeedClient
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    // Several podcast CDNs answer 403, or serve a redirect page instead of the
    // feed, to a client that sends no User-Agent — and .NET sends none by default.
    // Identify ourselves here rather than leaving each caller to remember.
    private const string UserAgent = "FocusPhone-Podcasts/1.0 (+https://storyworthtelling.com)";

    private readonly HttpClient http;

    public RssFeedClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<PodcastFeed> FetchAsync(Uri feedUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        var response = await http.SendAsync(request);
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

        var root = document.Root
            ?? throw new InvalidDataException($"Feed {feedUrl} is empty.");

        // RSS keeps the show under <channel>; Atom hangs entries off the root.
        var channel = root.Element("channel");
        var (container, items) = channel is not null
            ? (channel, channel.Elements("item"))
            : root.Name == Atom + "feed"
                ? (root, root.Elements(Atom + "entry"))
                : throw new InvalidDataException(
                    $"Feed {feedUrl} is not valid RSS or Atom: no <channel> or <feed> element.");

        var showImage = ImageUrl(container);

        var episodes = items
            .Select(item => ParseItem(item, feedUrl, showImage))
            .Where(episode => episode is not null)
            .Select(episode => episode!)
            .ToList();

        return new PodcastFeed(Title(container) ?? feedUrl.ToString(), episodes);
    }

    private static string? Title(XElement element) =>
        element.Element("title")?.Value ?? element.Element(Atom + "title")?.Value;

    private static Episode? ParseItem(XElement item, Uri feedUrl, string? showImage)
    {
        var audioUrl = AudioUrl(item);
        if (audioUrl is null)
        {
            return null;
        }

        var id = item.Element("guid")?.Value ?? item.Element(Atom + "id")?.Value ?? audioUrl;
        var title = Title(item) ?? id;

        // RSS dates are RFC-822, Atom's are RFC-3339; TryParse takes both.
        var published = item.Element("pubDate")?.Value
            ?? item.Element(Atom + "published")?.Value
            ?? item.Element(Atom + "updated")?.Value;
        var publishedUtc = DateTimeOffset.TryParse(published, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.MinValue;

        return new Episode(
            id,
            feedUrl.ToString(),
            title,
            publishedUtc,
            ParseDuration(item.Element(Itunes + "duration")?.Value),
            audioUrl,
            Description(item),
            ImageUrl(item) ?? showImage);
    }

    /// <summary>
    /// Every place a feed may put the audio, in order of how much it means it.
    /// A typed audio enclosure is the strongest claim; a typeless one is still
    /// almost always the episode, so it is taken before giving up.
    /// </summary>
    private static string? AudioUrl(XElement item)
    {
        var enclosures = item.Elements("enclosure").ToList();

        return FirstUrl(enclosures.Where(IsAudioTyped), "url")
            ?? FirstUrl(enclosures.Where(e => e.Attribute("type") is null), "url")
            ?? FirstUrl(MediaContent(item).Where(IsAudioMedia), "url")
            ?? FirstUrl(item.Elements(Atom + "link")
                .Where(link => link.Attribute("rel")?.Value == "enclosure"), "href")
            ?? FirstUrl(enclosures, "url");
    }

    private static IEnumerable<XElement> MediaContent(XElement item) =>
        item.Elements(Media + "content")
            .Concat(item.Elements(Media + "group").Elements(Media + "content"));

    private static bool IsAudioTyped(XElement element) =>
        element.Attribute("type")?.Value.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAudioMedia(XElement element) =>
        IsAudioTyped(element)
        || element.Attribute("medium")?.Value.Equals("audio", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FirstUrl(IEnumerable<XElement> elements, string attribute)
    {
        foreach (var element in elements)
        {
            var value = element.Attribute(attribute)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Show notes, as plain prose. Feeds disagree about which field is canonical,
    /// so the order is most-likely-to-be-a-clean-summary first; all three routinely
    /// contain HTML, which <see cref="FeedText"/> strips.
    /// </summary>
    private static string? Description(XElement item) =>
        FeedText.ToPlainText(item.Element("description")?.Value)
        ?? FeedText.ToPlainText(item.Element(Itunes + "summary")?.Value)
        ?? FeedText.ToPlainText(item.Element(Content + "encoded")?.Value)
        ?? FeedText.ToPlainText(item.Element(Atom + "summary")?.Value)
        ?? FeedText.ToPlainText(item.Element(Atom + "content")?.Value);

    /// <summary>
    /// Artwork for an item or, when read against the channel, for the whole show.
    /// itunes:image carries it in an attribute; the older RSS &lt;image&gt; wraps
    /// a &lt;url&gt; element.
    /// </summary>
    private static string? ImageUrl(XElement element)
    {
        var itunes = element.Element(Itunes + "image")?.Attribute("href")?.Value;
        if (!string.IsNullOrWhiteSpace(itunes))
        {
            return itunes;
        }

        var legacy = element.Element("image")?.Element("url")?.Value;
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
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
