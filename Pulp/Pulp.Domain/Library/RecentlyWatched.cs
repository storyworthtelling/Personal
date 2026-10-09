using Pulp.Domain.Core;
using Pulp.Domain.Watching;

namespace Pulp.Domain.Library;

/// <summary>
/// How a film stands, which is what its row says (mockup 5) — read off <see cref="Watched.Decide"/>'s
/// verdict, so a mark by hand either way says so (D278).
/// </summary>
public enum FilmEnding
{
    Finished,
    Stopped,
    MarkedWatched,
    MarkedUnwatched,
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
            .OfType<RecentRow>()
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

    private static RecentRow? Row(IReadOnlyList<WatchEvent> ordered, IReadOnlyDictionary<TitleId, int> episodeCounts)
    {
        // Marking unwatched is not a sitting: it changes how the title stands, not when it was last
        // watched. A title with nothing but such marks was never watched and is not listed.
        var last = ordered.LastOrDefault(e => e.Evidence != WatchSource.MarkedUnwatched);
        if (last is null)
        {
            return null;
        }

        if (last.Kind == WatchedKind.Film)
        {
            var verdict = Sittings.Verdict(ordered);
            var ending = verdict.Reason switch
            {
                WatchedReason.MarkedWatched => FilmEnding.MarkedWatched,
                WatchedReason.MarkedUnwatched => FilmEnding.MarkedUnwatched,
                _ => verdict.IsWatched ? FilmEnding.Finished : FilmEnding.Stopped,
            };
            var progress = ending == FilmEnding.Stopped ? Sittings.Progress(ordered) : null;
            return new RecentRow(last.Title, TitleKind.Film, last.At, last.Service, LastEpisode: null, progress, ShowFinished: false, ending);
        }

        var lastEpisode = ordered.LastOrDefault(e => e.Episode is not null && e.Evidence != WatchSource.MarkedUnwatched)?.Episode;
        var watchedCount = ContinueWatching.WatchedEpisodes(ordered).Count;

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
