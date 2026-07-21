namespace FocusPhone.Domain.Audio;

/// <summary>
/// Arranges the pool of unplayed episodes into a play order. Each strategy is a
/// pure function of the input episodes so the ordering is fully unit-testable.
/// </summary>
public interface IEpisodeQueueStrategy
{
    IReadOnlyList<Episode> Arrange(IReadOnlyList<Episode> unplayed);
}

/// <summary>
/// All unplayed episodes, oldest first, across every show together.
/// </summary>
public sealed class ChronologicalStrategy : IEpisodeQueueStrategy
{
    public IReadOnlyList<Episode> Arrange(IReadOnlyList<Episode> unplayed) =>
        [.. unplayed.OrderBy(episode => episode.PublishedUtc)];
}

/// <summary>
/// Alternates one unplayed episode per show each round, oldest first within each
/// show. Shows are cycled in the order of their oldest unplayed episode so the
/// result is deterministic. Gives every subscription a turn instead of letting a
/// prolific show dominate.
/// </summary>
public sealed class RoundRobinStrategy : IEpisodeQueueStrategy
{
    public IReadOnlyList<Episode> Arrange(IReadOnlyList<Episode> unplayed)
    {
        var shows = GroupByShowOldestFirst(unplayed)
            .OrderBy(show => show[0].PublishedUtc)
            .ToList();

        var result = new List<Episode>();
        for (var round = 0; ; round++)
        {
            var emitted = false;
            foreach (var show in shows)
            {
                if (round < show.Count)
                {
                    result.Add(show[round]);
                    emitted = true;
                }
            }

            if (!emitted)
            {
                break;
            }
        }

        return result;
    }

    private static List<List<Episode>> GroupByShowOldestFirst(IReadOnlyList<Episode> unplayed) =>
        [.. unplayed
            .GroupBy(episode => episode.FeedId)
            .Select(group => group.OrderBy(episode => episode.PublishedUtc).ToList())];
}

/// <summary>
/// Clears the shortest backlogs first: shows with the fewest unplayed episodes
/// come first (ties broken by the oldest unplayed episode), and each show is
/// emitted in full, oldest first, before the next show begins. Lets the user
/// finish and hide nearly-done shows quickly.
/// </summary>
public sealed class BacklogBusterStrategy : IEpisodeQueueStrategy
{
    public IReadOnlyList<Episode> Arrange(IReadOnlyList<Episode> unplayed) =>
        [.. unplayed
            .GroupBy(episode => episode.FeedId)
            .Select(group => group.OrderBy(episode => episode.PublishedUtc).ToList())
            .OrderBy(show => show.Count)
            .ThenBy(show => show[0].PublishedUtc)
            .SelectMany(show => show)];
}

/// <summary>
/// Resolves a <see cref="QueueArrangement"/> to its strategy.
/// <see cref="QueueArrangement.Manual"/> has no strategy (the user orders by hand).
/// </summary>
public static class EpisodeQueueStrategy
{
    public static IEpisodeQueueStrategy? For(QueueArrangement arrangement) => arrangement switch
    {
        QueueArrangement.RoundRobin => new RoundRobinStrategy(),
        QueueArrangement.Chronological => new ChronologicalStrategy(),
        QueueArrangement.BacklogBuster => new BacklogBusterStrategy(),
        _ => null,
    };
}
