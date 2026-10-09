using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>How a film's last sitting ended, which is what its row says (mockup 5).</summary>
public enum FilmEnding
{
    Finished,
    Stopped,
    MarkedWatched,
}

public sealed record RecentRow(
    TitleId Title,
    TitleKind Kind,
    DateTimeOffset LastWatchedAt,
    ServiceId? Service,
    EpisodeRef? LastEpisode,
    ProgressMark? Progress,
    bool ShowFinished,
    FilmEnding? Ending);

/// <summary>The rows last watched on one day, in the viewer's own zone.</summary>
public sealed record RecentDay(DateOnly Day, IReadOnlyList<RecentRow> Rows);

/// <summary>
/// Recently watched, one viewer's own record (D97). D226: nothing ages out. D227: one row per show,
/// under the day it was last watched in the viewer's zone, with the last episode and — until the show
/// is complete — a mark across the whole show. D228: films and shows only. Newest first.
/// </summary>
public static class RecentlyWatched
{
    public static IReadOnlyList<RecentDay> Build(IEnumerable<WatchEvent> events, IReadOnlyDictionary<TitleId, int> episodeCounts, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(episodeCounts);
        ArgumentNullException.ThrowIfNull(zone);

        var rows = events
            .Where(e => e.Kind != WatchedKind.Game)
            .GroupBy(e => e.Title)
            .Select(sittings => Row(sittings.OrderBy(e => e.At).ToList(), episodeCounts))
            .ToList();

        return rows
            .GroupBy(row => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.LastWatchedAt, zone).DateTime))
            .OrderByDescending(day => day.Key)
            .Select(day => new RecentDay(
                day.Key,
                day.OrderByDescending(row => row.LastWatchedAt)
                    .ThenBy(row => row.Title.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    private static RecentRow Row(IReadOnlyList<WatchEvent> ordered, IReadOnlyDictionary<TitleId, int> episodeCounts)
    {
        var last = ordered[^1];
        if (last.Kind == WatchedKind.Film)
        {
            var ending = last.Evidence == WatchSource.Manual
                ? FilmEnding.MarkedWatched
                : last.Completed ? FilmEnding.Finished : FilmEnding.Stopped;
            var progress = ending == FilmEnding.Stopped ? ContinueWatching.Minutes(last) : null;
            return new RecentRow(last.Title, TitleKind.Film, last.At, last.Service, LastEpisode: null, progress, ShowFinished: false, ending);
        }

        var lastEpisode = ordered.LastOrDefault(e => e.Episode is not null)?.Episode;
        var watchedCount = ordered
            .Where(e => e.Episode is not null && e.Completed)
            .Select(e => e.Episode!)
            .Distinct()
            .Count();

        var finished = false;
        ProgressMark? mark = null;
        if (episodeCounts.TryGetValue(last.Title, out var count) && count > 0)
        {
            finished = watchedCount >= count;
            mark = finished ? null : new ProgressMark(watchedCount, count, ProgressUnit.Episode);
        }

        return new RecentRow(last.Title, TitleKind.Show, last.At, last.Service, lastEpisode, mark, finished, Ending: null);
    }
}
