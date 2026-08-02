using System.Xml.Linq;
using FocusPhone.Domain.Audio;

namespace FocusPhone.Infrastructure.Podcasts;

/// <summary>
/// Parses a Podcast Addict (or any) OPML export into subscriptions for a one-shot
/// day-one import. Outlines may be nested under category outlines, so every
/// outline carrying an <c>xmlUrl</c> anywhere in the tree is taken; duplicates by
/// feed URL are collapsed. Pure — no I/O — so it is fixture-tested.
/// </summary>
public static class OpmlImportParser
{
    public static IReadOnlyList<OpmlSubscription> Parse(string opml)
    {
        var document = XDocument.Parse(opml);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subscriptions = new List<OpmlSubscription>();
        foreach (var outline in document.Descendants("outline"))
        {
            var feedUrl = outline.Attribute("xmlUrl")?.Value;
            if (string.IsNullOrWhiteSpace(feedUrl) || !seen.Add(feedUrl))
            {
                continue;
            }

            var title = outline.Attribute("text")?.Value
                ?? outline.Attribute("title")?.Value
                ?? feedUrl;
            subscriptions.Add(new OpmlSubscription(title, feedUrl));
        }

        return subscriptions;
    }
}
