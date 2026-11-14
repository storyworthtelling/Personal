using Pulp.Domain.Services;

namespace Pulp.Domain.HandOffs;

/// <summary>A watched hand-off whose landing is decided, with whatever the head tagged it with.</summary>
public sealed record SettledLanding(PendingHandOff HandOff, LandingVerdict Verdict, object Tag);

/// <summary>
/// Holds started hand-offs and the playback sessions relayed after them until <see cref="LandingCheck"/>
/// has a final answer (D122, orphan audit §28). A hand-off settles as soon as its title is seen playing
/// — nothing later can change that — and otherwise when the window closes, since a trailer playing
/// first is not yet a mis-landing. Not thread-safe; the head holds one behind a lock.
/// </summary>
public sealed class LandingWatch
{
    private readonly List<(PendingHandOff HandOff, object Tag)> watched = [];
    private readonly List<PlaybackSession> sessions = [];

    public int SessionCount => sessions.Count;

    public void Watch(PendingHandOff handOff, object tag)
    {
        ArgumentNullException.ThrowIfNull(handOff);
        ArgumentNullException.ThrowIfNull(tag);
        watched.Add((handOff, tag));
        Prune();
    }

    public void Saw(PlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        sessions.Add(session);
        Prune();
    }

    public IReadOnlyList<SettledLanding> Settle(ServiceCatalogue catalogue, DateTimeOffset now)
    {
        var settled = new List<SettledLanding>();
        foreach (var entry in watched.ToList())
        {
            var verdict = LandingCheck.Decide(entry.HandOff, sessions, catalogue, now);
            var windowClosed = now >= entry.HandOff.Evidence.At + LandingCheck.Window;
            if (verdict.Landing == Landing.Landed || windowClosed)
            {
                watched.Remove(entry);
                settled.Add(new SettledLanding(entry.HandOff, verdict, entry.Tag));
            }
        }
        Prune();
        return settled;
    }

    // A session is evidence only for a hand-off made before it, so one older than every watched
    // hand-off can never count; with nothing watched, nothing is kept.
    private void Prune()
    {
        if (watched.Count == 0)
        {
            sessions.Clear();
            return;
        }
        var earliest = watched.Min(entry => entry.HandOff.Evidence.At);
        sessions.RemoveAll(session => session.At < earliest);
    }
}
