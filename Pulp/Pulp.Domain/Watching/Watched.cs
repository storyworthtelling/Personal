namespace Pulp.Domain.Watching;

/// <summary>Pulp handed this title or episode off to its service (D10) — the fallback evidence (D30).</summary>
public sealed record HandedOff(DateTimeOffset At);

/// <summary>
/// One reading of the playing app's media session for this title or episode (finding §2.3): where
/// it was and how long it is, and whether the app reported the end. Read by Pulp's own notification
/// listener on an unrestricted phone, or relayed by Flyleaf on the locked one.
/// </summary>
public sealed record SessionObservation
{
    public SessionObservation(DateTimeOffset at, TimeSpan position, TimeSpan duration, bool ended)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        At = at;
        Position = position;
        Duration = duration;
        Ended = ended;
    }

    public DateTimeOffset At { get; }

    public TimeSpan Position { get; }

    /// <summary>Zero when the app did not publish one (a live game, or a service that leaves it out).</summary>
    public TimeSpan Duration { get; }

    /// <summary>The app reported its session finished.</summary>
    public bool Ended { get; }
}

/// <summary>The viewer marked it watched, or unwatched, by hand (D10, D30, D182).</summary>
public sealed record ManualMark(DateTimeOffset At, bool Watched);

/// <summary>
/// Everything known about one viewer and one title or episode. The caller selects it — by viewer,
/// since history is per viewer (D10, D194), and by episode for a show — so the rule stays about
/// evidence alone.
/// </summary>
public sealed class WatchEvidence
{
    public WatchEvidence(
        IEnumerable<HandedOff> handOffs,
        IEnumerable<SessionObservation> sessions,
        IEnumerable<ManualMark> marks)
    {
        HandOffs = (handOffs ?? throw new ArgumentNullException(nameof(handOffs))).ToList();
        Sessions = (sessions ?? throw new ArgumentNullException(nameof(sessions))).ToList();
        Marks = (marks ?? throw new ArgumentNullException(nameof(marks))).ToList();
    }

    public IReadOnlyList<HandedOff> HandOffs { get; }

    public IReadOnlyList<SessionObservation> Sessions { get; }

    public IReadOnlyList<ManualMark> Marks { get; }
}

/// <summary>Why a verdict came out as it did — so a screen or the diagnostics can say which evidence spoke.</summary>
public enum WatchedReason
{
    NoEvidence,
    MarkedWatched,
    MarkedUnwatched,
    SessionReachedEnd,
    SessionStoppedShort,
    HandedOff,
}

public readonly record struct WatchedVerdict(bool IsWatched, WatchedReason Reason);

/// <summary>
/// Decides whether a viewer has watched a title or episode (D10, D30, D182).
///
/// <list type="number">
/// <item><b>A manual mark wins</b> over every piece of evidence at or before it; the latest mark is
/// the one that counts. Evidence <i>after</i> the mark speaks again — marking a title unwatched to
/// see it again and then seeing it to the end is learned, rather than leaving the title stuck
/// unwatched for good.</item>
/// <item><b>The media session comes first</b> among the rest: any session that reached its end means
/// watched; sessions that were seen but all stopped short mean not watched, whatever the hand-off
/// says, because the service is publishing and said so.</item>
/// <item><b>The hand-off is the fallback</b>: handed off with no session seen means watched (D30,
/// D182) — the case for every service that publishes nothing to its session on the phone.</item>
/// </list>
///
/// <para>Deliberately simple where sessions are patchy: a session that stopped short on Monday
/// outvotes a Tuesday hand-off whose session the relay missed, so the title stays on Continue
/// Watching rather than being marked watched on guesswork. A manual mark corrects either way.</para>
/// </summary>
public static class Watched
{
    /// <summary>
    /// How far through a session counts as its end: the closing credits are skipped, stopped or
    /// auto-advanced past, so "reaches its end" (D182) cannot mean the last second. <b>Un-evidenced:</b>
    /// no service's session has been read on a device yet (finding §4); this is the value to tune.
    /// </summary>
    public const double EndFraction = 0.95;

    public static WatchedVerdict Decide(WatchEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var lastMark = evidence.Marks.OrderBy(mark => mark.At).LastOrDefault();
        var since = lastMark?.At;
        bool After(DateTimeOffset at) => since is null || at > since.Value;

        var sessions = evidence.Sessions.Where(session => After(session.At)).ToList();
        if (sessions.Any(ReachedEnd))
        {
            return new WatchedVerdict(true, WatchedReason.SessionReachedEnd);
        }

        if (sessions.Count > 0)
        {
            return new WatchedVerdict(false, WatchedReason.SessionStoppedShort);
        }

        if (evidence.HandOffs.Any(handOff => After(handOff.At)))
        {
            return new WatchedVerdict(true, WatchedReason.HandedOff);
        }

        if (lastMark is not null)
        {
            return lastMark.Watched
                ? new WatchedVerdict(true, WatchedReason.MarkedWatched)
                : new WatchedVerdict(false, WatchedReason.MarkedUnwatched);
        }

        return new WatchedVerdict(false, WatchedReason.NoEvidence);
    }

    private static bool ReachedEnd(SessionObservation session) =>
        session.Ended
        || (session.Duration > TimeSpan.Zero && session.Position.Ticks >= session.Duration.Ticks * EndFraction);
}
