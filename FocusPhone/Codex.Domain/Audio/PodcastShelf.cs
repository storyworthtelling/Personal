namespace Codex.Domain.Audio;

/// <summary>One show on the home shelf: its unplayed count and the freshest unplayed episode's date.</summary>
public record ShowSummary(string FeedId, string Title, int UnplayedCount, DateTimeOffset LatestUnplayedUtc);

/// <summary>
/// Builds the podcast home — one row per show, hiding shows with nothing unplayed
/// (the owner's hide-fully-read behaviour) and ordering by the freshest thing they
/// haven't heard. Pure, so it is unit-tested; the activity just renders the result.
/// </summary>
public static class PodcastShelf
{
    /// <summary>
    /// The set of finished episode ids: an episode is done if it is in the legacy
    /// listened set OR its playback progress has reached the end. Unifies the old
    /// manual flag with the new resume-based completion.
    /// </summary>
    public static IReadOnlySet<string> DoneEpisodes(
        IEnumerable<Episode> episodes,
        IEnumerable<string> listenedIds,
        PlaybackProgressBook progress)
    {
        var done = new HashSet<string>(listenedIds);
        foreach (var episode in episodes)
        {
            if (progress.IsComplete(episode.Id))
            {
                done.Add(episode.Id);
            }
        }

        return done;
    }

    public static IReadOnlyList<ShowSummary> Build(
        IEnumerable<Episode> episodes,
        IReadOnlyDictionary<string, string> feedTitles,
        IReadOnlySet<string> doneIds) =>
        [.. episodes
            .GroupBy(episode => episode.FeedId)
            .Select(group => BuildSummary(group, feedTitles, doneIds))
            .Where(summary => summary.UnplayedCount > 0)
            .OrderByDescending(summary => summary.LatestUnplayedUtc)];

    private static ShowSummary BuildSummary(
        IGrouping<string, Episode> group,
        IReadOnlyDictionary<string, string> feedTitles,
        IReadOnlySet<string> doneIds)
    {
        var unplayed = group.Where(episode => !doneIds.Contains(episode.Id)).ToList();
        var latest = unplayed.Count > 0
            ? unplayed.Max(episode => episode.PublishedUtc)
            : DateTimeOffset.MinValue;
        var title = feedTitles.TryGetValue(group.Key, out var value) ? value : group.Key;
        return new ShowSummary(group.Key, title, unplayed.Count, latest);
    }
}
