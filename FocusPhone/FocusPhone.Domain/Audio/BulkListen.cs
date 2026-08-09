namespace FocusPhone.Domain.Audio;

/// <summary>
/// Pure selection of which episode ids a bulk "mark played" gesture covers, so the
/// audio activity's long-press menus mark exactly the right set. Kept separate from
/// <see cref="PodcastLibrary"/> because the activity holds its listened state as a
/// raw id set rather than a library instance.
/// </summary>
public static class BulkListen
{
    /// <summary>
    /// Every episode of one show — the ids to mark when a whole show is marked played
    /// (long-press on a shelf row).
    /// </summary>
    public static IReadOnlyList<string> AllInShow(IEnumerable<Episode> episodes, string feedId) =>
        [.. episodes
            .Where(episode => episode.FeedId == feedId)
            .Select(episode => episode.Id)];

    /// <summary>
    /// Every episode of the anchor's show published strictly before it — the ids to
    /// clear when older episodes are marked played from beneath one in the list. The
    /// anchor itself is left untouched.
    /// </summary>
    public static IReadOnlyList<string> OlderInShow(IEnumerable<Episode> episodes, Episode anchor) =>
        [.. episodes
            .Where(episode => episode.FeedId == anchor.FeedId
                && episode.PublishedUtc < anchor.PublishedUtc)
            .Select(episode => episode.Id)];
}
