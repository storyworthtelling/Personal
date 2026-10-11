using Pulp.Domain.Core;
using Pulp.Domain.Watching;

namespace Pulp.Domain.Library;

/// <summary>
/// One row on Continue Watching. <see cref="Since"/> is what it is ordered by — the last sitting, or
/// the arrival of an episode that came out after it. A show whose schedule Pulp does not know has no
/// next-up or mark.
/// </summary>
public sealed record ContinueRow(
    TitleId Title,
    TitleKind Kind,
    DateTimeOffset Since,
    bool NewEpisode,
    EpisodeRef? NextUp,
    ProgressMark? Progress);

/// <summary>A viewer's "Remove from Continue watching" of one title, and when they did it.</summary>
public sealed record ContinueDismissal(TitleId Title, DateTimeOffset At);

/// <summary>
/// Continue Watching for one viewer (private, D97). D96: most recently watched first, and an episode
/// that came out after the last sitting jumps its show to the top. D152: a show with nothing left out
/// leaves, and comes back when an episode lands. D153: a finished film, or a show watched to its end,
/// leaves — "finished" and "marked unwatched" both being <see cref="Watched.Decide"/>'s verdict
/// (D278, D279), so a title marked unwatched comes back. Games are the Sports rules' business, not this list's. The clock is an argument.
///
/// <para><b>Removed by hand</b> (D31: a show stays "until removed"). A removed title stays off until
/// the viewer sits down with it again — a session or a hand-off <b>newer than the removal</b>. A new
/// episode airing does not bring it back: D152's return on a new episode is for a show that left
/// because it was caught up, which says nothing about one the viewer took off on purpose, and D153's
/// "for good" is the stronger of the two departures, not the weaker. A manual mark is not a sitting
/// (D278 — it corrects the record, it is not watching), so marking an episode does not bring the show
/// back either. A removed show's new episodes still reach the calendar (D21); only this list is
/// quiet about them.</para>
/// </summary>
public static class ContinueWatching
{
    /// <summary>Continue Watching with nothing removed by hand.</summary>
    public static IReadOnlyList<ContinueRow> Build(IEnumerable<WatchEvent> events, IEnumerable<ShowSchedule> schedules, DateTimeOffset now) =>
        Build(events, schedules, [], now);

    /// <summary>Continue Watching, leaving out each title removed by hand until a newer sitting of it.</summary>
    public static IReadOnlyList<ContinueRow> Build(
        IEnumerable<WatchEvent> events,
        IEnumerable<ShowSchedule> schedules,
        IEnumerable<ContinueDismissal> dismissals,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(dismissals);

        var removedAt = new Dictionary<TitleId, DateTimeOffset>();
        foreach (var dismissal in dismissals)
        {
            if (!removedAt.TryGetValue(dismissal.Title, out var at) || dismissal.At > at)
            {
                removedAt[dismissal.Title] = dismissal.At;
            }
        }

        var scheduleByTitle = new Dictionary<TitleId, ShowSchedule>();
        foreach (var schedule in schedules)
        {
            scheduleByTitle[schedule.Title] = schedule;
        }

        var rows = new List<ContinueRow>();
        foreach (var sittings in events.Where(e => e.Kind != WatchedKind.Game).GroupBy(e => e.Title))
        {
            var ordered = sittings.OrderBy(e => e.At).ToList();
            if (removedAt.TryGetValue(sittings.Key, out var removed) && !SatWithSince(ordered, removed))
            {
                continue;
            }

            var last = ordered[^1];
            var row = last.Kind == WatchedKind.Film
                ? FilmRow(ordered)
                : ShowRow(ordered, scheduleByTitle.GetValueOrDefault(sittings.Key), now);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows
            .OrderByDescending(row => row.Since)
            .ThenBy(row => row.Title.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether the viewer sat down with the title after removing it: a session or a hand-off, never a mark (D278).</summary>
    private static bool SatWithSince(IEnumerable<WatchEvent> ordered, DateTimeOffset removed) =>
        ordered.Any(sitting => sitting.At > removed && sitting.Evidence is WatchSource.Session or WatchSource.HandOff);

    private static ContinueRow? FilmRow(IReadOnlyList<WatchEvent> ordered)
    {
        if (Sittings.Verdict(ordered).IsWatched)
        {
            return null;
        }

        var last = ordered[^1];
        return new ContinueRow(last.Title, TitleKind.Film, last.At, NewEpisode: false, NextUp: null, Sittings.Progress(ordered));
    }

    /// <summary>The episodes of a show the viewer has watched, each by <see cref="Sittings.Verdict"/> over its own sittings.</summary>
    internal static HashSet<EpisodeRef> WatchedEpisodes(IEnumerable<WatchEvent> ordered) =>
        ordered
            .Where(e => e.Episode is not null)
            .GroupBy(e => e.Episode!)
            .Where(episode => Sittings.Verdict(episode).IsWatched)
            .Select(episode => episode.Key)
            .ToHashSet();

    private static ContinueRow? ShowRow(IReadOnlyList<WatchEvent> ordered, ShowSchedule? schedule, DateTimeOffset now)
    {
        var last = ordered[^1];
        if (schedule is null)
        {
            // D31: with no schedule Pulp cannot tell whether anything is left, so the show stays.
            return new ContinueRow(last.Title, TitleKind.Show, last.At, NewEpisode: false, NextUp: null, Progress: null);
        }

        var watched = WatchedEpisodes(ordered);
        var furthest = watched.Max();

        var left = schedule.Episodes
            .Where(e => e.AirsAt <= now)
            .Where(e => !watched.Contains(e.Episode) && (furthest is null || e.Episode.CompareTo(furthest) > 0))
            .OrderBy(e => e.Episode)
            .ToList();
        if (left.Count == 0)
        {
            return null;
        }

        var arrived = left.Where(e => e.AirsAt > last.At).ToList();
        var since = arrived.Count == 0 ? last.At : arrived.Max(e => e.AirsAt);
        var nextUp = left[0].Episode;
        var inSeason = schedule.Episodes.Count(e => e.Episode.Season == nextUp.Season);

        return new ContinueRow(
            last.Title,
            TitleKind.Show,
            since,
            NewEpisode: arrived.Count > 0,
            nextUp,
            new ProgressMark(nextUp.Number, inSeason, ProgressUnit.Episode));
    }
}
