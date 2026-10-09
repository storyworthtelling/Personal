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

/// <summary>
/// Continue Watching for one viewer (private, D97). D96: most recently watched first, and an episode
/// that came out after the last sitting jumps its show to the top. D152: a show with nothing left out
/// leaves, and comes back when an episode lands. D153: a finished film, or a show watched to its end,
/// leaves — "finished" and "marked unwatched" both being <see cref="Watched.Decide"/>'s verdict
/// (D278, D279), so a title marked unwatched comes back. Games are the Sports rules' business, not this list's. The clock is an argument.
/// </summary>
public static class ContinueWatching
{
    public static IReadOnlyList<ContinueRow> Build(IEnumerable<WatchEvent> events, IEnumerable<ShowSchedule> schedules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(schedules);

        var scheduleByTitle = new Dictionary<TitleId, ShowSchedule>();
        foreach (var schedule in schedules)
        {
            scheduleByTitle[schedule.Title] = schedule;
        }

        var rows = new List<ContinueRow>();
        foreach (var sittings in events.Where(e => e.Kind != WatchedKind.Game).GroupBy(e => e.Title))
        {
            var ordered = sittings.OrderBy(e => e.At).ToList();
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
