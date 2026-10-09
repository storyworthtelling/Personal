using Pulp.Domain.Core;

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
/// leaves. Games are the Sports rules' business, not this list's. The clock is an argument.
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
                ? FilmRow(last)
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

    private static ContinueRow? FilmRow(WatchEvent last)
    {
        if (last.Completed)
        {
            return null;
        }

        return new ContinueRow(last.Title, TitleKind.Film, last.At, NewEpisode: false, NextUp: null, Minutes(last));
    }

    private static ContinueRow? ShowRow(IReadOnlyList<WatchEvent> ordered, ShowSchedule? schedule, DateTimeOffset now)
    {
        var last = ordered[^1];
        if (schedule is null)
        {
            // D31: with no schedule Pulp cannot tell whether anything is left, so the show stays.
            return new ContinueRow(last.Title, TitleKind.Show, last.At, NewEpisode: false, NextUp: null, Progress: null);
        }

        var watched = ordered
            .Where(e => e.Episode is not null && e.Completed)
            .Select(e => e.Episode!)
            .ToHashSet();
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

    internal static ProgressMark? Minutes(WatchEvent sitting) =>
        sitting.StoppedAt is { } at && sitting.Runtime is { } runtime
            ? new ProgressMark((int)at.TotalMinutes, (int)runtime.TotalMinutes, ProgressUnit.Minute)
            : null;
}
