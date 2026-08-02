namespace FocusPhone.Domain.Music;

/// <summary>
/// The rolling cap on the on-device listening history: keep only the events belonging to the most recent
/// <c>maxSessions</c> distinct sittings, so the raw history can't grow unbounded while whole sessions stay
/// intact (trimming by event count would sever a session mid-sitting and skew the reduce). Pure.
/// </summary>
public static class ListeningHistory
{
    public static IReadOnlyList<ListeningEvent> Trim(IReadOnlyList<ListeningEvent> events, int maxSessions)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (maxSessions <= 0)
        {
            return [];
        }

        var latestBySession = new Dictionary<string, DateTimeOffset>();
        foreach (var listeningEvent in events)
        {
            if (!latestBySession.TryGetValue(listeningEvent.SessionId, out var latest) || listeningEvent.AtUtc > latest)
            {
                latestBySession[listeningEvent.SessionId] = listeningEvent.AtUtc;
            }
        }

        if (latestBySession.Count <= maxSessions)
        {
            return [.. events];
        }

        var kept = latestBySession
            .OrderByDescending(pair => pair.Value)
            .Take(maxSessions)
            .Select(pair => pair.Key)
            .ToHashSet();

        return [.. events.Where(listeningEvent => kept.Contains(listeningEvent.SessionId))];
    }
}
