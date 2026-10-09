using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>What was watched. Games are recorded too, so the lists that exclude them can say so (D228).</summary>
public enum WatchedKind
{
    Film,
    Show,
    Game,
}

/// <summary>How Pulp knows (D10, D30): a playback session reported it, a hand-off was assumed, or the viewer marked it.</summary>
public enum WatchSource
{
    Session,
    HandOff,
    Manual,
}

/// <summary>An episode by season and number, ordered season first.</summary>
public sealed record EpisodeRef(int Season, int Number) : IComparable<EpisodeRef>
{
    public int CompareTo(EpisodeRef? other)
    {
        if (other is null)
        {
            return 1;
        }

        var bySeason = Season.CompareTo(other.Season);
        return bySeason != 0 ? bySeason : Number.CompareTo(other.Number);
    }
}

/// <summary>
/// One viewer's one sitting with a title — the narrow record the Library's lists are built from.
/// A sitting with a stop point short of the runtime was not finished; one marked by hand was.
/// </summary>
public sealed record WatchEvent(
    TitleId Title,
    WatchedKind Kind,
    DateTimeOffset At,
    ServiceId? Service,
    EpisodeRef? Episode = null,
    WatchSource Evidence = WatchSource.Session,
    TimeSpan? StoppedAt = null,
    TimeSpan? Runtime = null)
{
    public bool Completed =>
        Evidence == WatchSource.Manual || StoppedAt is null || Runtime is null || StoppedAt >= Runtime;
}
