namespace Codex.Domain.Music;

/// <summary>
/// Reduces a flat list of <see cref="ListeningEvent"/>s into <see cref="TasteEvidence"/> — the "reduce"
/// half of the Taste Profile, modeled on Storyboarder's DesignBibleMerger. Affinities are ranked by the
/// number of DISTINCT sessions that touched a key (a <see cref="HashSet{T}"/> of session ids, not a raw
/// counter), so a single long sitting can't inflate a genre the way counting plays would.
/// </summary>
public static class TasteEvidenceBuilder
{
    public static TasteEvidence Build(IReadOnlyList<ListeningEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return TasteEvidence.Empty;
        }

        var histogram = new int[24];
        foreach (var listeningEvent in events)
        {
            if (listeningEvent.LocalHour is >= 0 and < 24)
            {
                histogram[listeningEvent.LocalHour]++;
            }
        }

        var sessionsAnalyzed = events.Select(e => e.SessionId).Distinct().Count();

        return new TasteEvidence(
            RankByKey(events, e => e.Genre),
            RankByKey(events, e => e.ArtistName),
            RankByKey(events, e => e.AlbumArtist),
            histogram,
            sessionsAnalyzed);
    }

    private static IReadOnlyList<AffinityStat> RankByKey(
        IReadOnlyList<ListeningEvent> events, Func<ListeningEvent, string> keySelector) =>
        events
            .GroupBy(keySelector)
            .Select(group => new AffinityStat(
                group.Key,
                Sessions: group.Select(e => e.SessionId).Distinct().Count(),
                Completions: group.Count(e => e.Signal == ListeningSignal.Completed),
                Skips: group.Count(e => e.Signal == ListeningSignal.Skipped),
                Replays: group.Count(e => e.Signal == ListeningSignal.Replayed),
                AlbumPlays: group.Count(e => e.Signal == ListeningSignal.PlayedFromAlbum),
                ShufflePlays: group.Count(e => e.Signal == ListeningSignal.PlayedFromShuffle)))
            .OrderByDescending(stat => stat.Sessions)
            .ThenByDescending(stat => stat.Completions + stat.Replays - stat.Skips)
            .ThenBy(stat => stat.Key, StringComparer.Ordinal)
            .ToList();
}
