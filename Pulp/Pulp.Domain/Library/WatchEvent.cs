using Pulp.Domain.Core;
using Pulp.Domain.Watching;

namespace Pulp.Domain.Library;

/// <summary>What was watched. Games are recorded too, so the lists that exclude them can say so (D228).</summary>
public enum WatchedKind
{
    Film,
    Show,
    Game,
}

/// <summary>How Pulp knows (D10, D30): a playback session reported it, a hand-off was assumed, or the viewer marked it watched or unwatched (D278).</summary>
public enum WatchSource
{
    Session,
    HandOff,
    MarkedWatched,
    MarkedUnwatched,
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
/// One viewer's one sitting with a title — the narrow record the Library's lists are built from. It
/// carries what was observed and nothing worked out from it: whether the title or episode counts as
/// watched is <see cref="Watched.Decide"/>'s verdict alone (D278, D279), reached through
/// <see cref="Sittings.Verdict"/>. A <see cref="WatchSource.Session"/> sitting is one reading of the
/// playing app's session — where it stopped, how long it is if known, and whether the app reported
/// the end; a hand-off or a mark carries only its moment.
/// </summary>
public sealed record WatchEvent(
    TitleId Title,
    WatchedKind Kind,
    DateTimeOffset At,
    ServiceId? Service,
    EpisodeRef? Episode = null,
    WatchSource Evidence = WatchSource.Session,
    TimeSpan? StoppedAt = null,
    TimeSpan? Runtime = null,
    bool Ended = false);

/// <summary>
/// The bridge from the Library's sittings to the one rule for "watched" (finding S2): the sittings of
/// one title, or of one episode of a show, become the Watching rule's evidence and the verdict is
/// <see cref="Watched.Decide"/>'s. There is deliberately no second test of "finished" here.
/// </summary>
public static class Sittings
{
    public static WatchEvidence Evidence(IEnumerable<WatchEvent> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);

        var handOffs = new List<HandedOff>();
        var sessions = new List<SessionObservation>();
        var marks = new List<ManualMark>();
        foreach (var sitting in sittings)
        {
            switch (sitting.Evidence)
            {
                case WatchSource.Session:
                    sessions.Add(new SessionObservation(
                        sitting.At,
                        sitting.StoppedAt ?? TimeSpan.Zero,
                        sitting.Runtime ?? TimeSpan.Zero,
                        sitting.Ended));
                    break;
                case WatchSource.HandOff:
                    handOffs.Add(new HandedOff(sitting.At));
                    break;
                case WatchSource.MarkedWatched:
                    marks.Add(new ManualMark(sitting.At, Watched: true));
                    break;
                case WatchSource.MarkedUnwatched:
                    marks.Add(new ManualMark(sitting.At, Watched: false));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sittings), sitting.Evidence, $"Sitting for {sitting.Title.Value} has an unknown watch source.");
            }
        }

        return new WatchEvidence(handOffs, sessions, marks);
    }

    public static WatchedVerdict Verdict(IEnumerable<WatchEvent> sittings) => Watched.Decide(Evidence(sittings));

    /// <summary>
    /// Where the viewer stopped, as minutes of the runtime: the latest session reading since the
    /// latest mark (D278 — a mark wipes the slate, so a title marked unwatched to see again starts
    /// without the old stop point). None when the runtime is unknown.
    /// </summary>
    public static ProgressMark? Progress(IEnumerable<WatchEvent> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);

        var ordered = sittings.OrderBy(sitting => sitting.At).ToList();
        var lastMark = ordered.LastOrDefault(IsMark)?.At;
        var reading = ordered.LastOrDefault(sitting =>
            sitting.Evidence == WatchSource.Session && (lastMark is null || sitting.At > lastMark.Value));

        return reading is { StoppedAt: { } at, Runtime: { } runtime }
            ? new ProgressMark((int)at.TotalMinutes, (int)runtime.TotalMinutes, ProgressUnit.Minute)
            : null;
    }

    internal static bool IsMark(WatchEvent sitting) =>
        sitting.Evidence is WatchSource.MarkedWatched or WatchSource.MarkedUnwatched;
}
