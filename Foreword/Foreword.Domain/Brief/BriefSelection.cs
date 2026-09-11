using Foreword.Domain.Models;

namespace Foreword.Domain.Brief;

/// <summary>
/// Which stories reach the brief, in what order, and what is said about the ones that failed.
///
/// <para>Three things the pipeline did not do. It printed the same story twice when two sections
/// carried it — Top Stories returns a piece under Home and again under its own section, so the owner
/// counted eight stories and found seven. It printed them in whatever order two API calls happened
/// to return, so an opinion column could open the paper, which is a different paper. And when a full
/// article failed to fetch it printed the abstract <b>silently</b>, which is bug B1.</para>
/// </summary>
public static class BriefSelection
{
    /// <summary>
    /// A placement rather than a subject. A folio reading "Home · 1 of 8" tells the reader nothing,
    /// so the copy filed under a real section is the one kept.
    /// </summary>
    private const string Placement = "Home";

    public static IReadOnlyList<TopStory> Choose(IReadOnlyList<TopStory> stories)
    {
        var seen = new Dictionary<string, TopStory>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var story in stories)
        {
            // Matched on the URL, which is the story's identity. The same piece is re-titled between
            // the home page and its section often enough that a title match would let it through.
            var key = story.Url;

            if (!seen.TryGetValue(key, out var kept))
            {
                seen[key] = story;
                order.Add(key);
                continue;
            }

            if (IsPlacement(kept.Section) && !IsPlacement(story.Section))
            {
                seen[key] = story;
            }
        }

        // News before opinion, and inside each half the desk's own order — an editorial judgement
        // made by people who do this for a living, which re-sorting would throw away for nothing.
        return
        [
            .. order.Select(key => seen[key])
                .OrderBy(story => IsOpinion(story.Section) ? 1 : 0),
        ];
    }

    /// <summary>
    /// The abstract standing in for a full article that did not retrieve, marked so the page can say
    /// so. Null when there is no abstract either — a page carrying a headline over white space is
    /// not a story, it is a fault printed at full size.
    /// </summary>
    public static ArticleContent? FromAbstract(TopStory story) =>
        string.IsNullOrWhiteSpace(story.Abstract)
            ? null
            : new ArticleContent
            {
                Title = story.Title,
                Byline = story.Byline,
                Section = story.Section,
                Body = [new TextBlock { Text = story.Abstract }],
                AbstractOnly = true,
            };

    private static bool IsPlacement(string section) =>
        string.Equals(section, Placement, StringComparison.OrdinalIgnoreCase);

    private static bool IsOpinion(string section) =>
        section.Contains("Opinion", StringComparison.OrdinalIgnoreCase);
}
