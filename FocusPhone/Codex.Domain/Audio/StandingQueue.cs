namespace Codex.Domain.Audio;

/// <summary>
/// The podcast queue as a <b>standing rule</b> rather than a one-off action.
///
/// Auto-arrange used to run once and then drift: it ordered whatever was unplayed
/// at that moment, and nothing rebuilt it afterwards. An episode that arrived the
/// next morning never joined the queue, and one that had been played sat in it
/// until something else cleared it. Worse, it replaced the queue outright, so
/// anything queued by hand was thrown away the next time it ran.
///
/// So the rule is re-applied whenever the pool changes — a feed refresh, a
/// mark-played — and it is a pure function of (arrangement, what was pinned, what
/// is unplayed), which is what makes that safe to do repeatedly.
///
/// <b>Pinned wins.</b> An episode queued by hand is an explicit instruction and
/// survives every rebuild, ahead of whatever the arrangement produced. First rather
/// than in place, because "in place" has no meaning once the rebuild has reordered
/// everything around it.
/// </summary>
public static class StandingQueue
{
    public static IReadOnlyList<string> Rebuild(
        QueueArrangement arrangement,
        IReadOnlyList<string> pinnedIds,
        IReadOnlyList<Episode> unplayed)
    {
        var available = unplayed.Select(episode => episode.Id).ToHashSet(StringComparer.Ordinal);

        // A pinned episode that has since been played is simply forgotten: the pin
        // says "queue this", not "keep this here after I have heard it".
        var pinned = pinnedIds.Where(available.Contains).Distinct(StringComparer.Ordinal).ToList();

        var strategy = EpisodeQueueStrategy.For(arrangement);
        if (strategy is null)
        {
            // Manual: the queue is exactly what was put in it.
            return pinned;
        }

        var alreadyQueued = pinned.ToHashSet(StringComparer.Ordinal);
        var arranged = strategy
            .Arrange([.. unplayed.Where(episode => !alreadyQueued.Contains(episode.Id))])
            .Select(episode => episode.Id);

        return [.. pinned, .. arranged];
    }
}
