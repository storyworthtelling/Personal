using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.Catalogue;

/// <summary>
/// The weekly refresh of tracked titles (D204, audit W7.5): what the background worker looks at again. A
/// title is tracked when a show is being watched (it has a sitting) or it is a film or show on any playlist;
/// it is due once <see cref="CatalogueFreshness"/> says a tracked title is stale. The worker runs daily, so
/// the week's load spreads out, and two limits keep it honest with the free allowance of 1,000 lookups a
/// month: no run asks about more than <see cref="PerRun"/>, and the background stops at
/// <see cref="BackgroundCeiling"/> so opening a title page can always still look.
/// </summary>
public static class WeeklyRefresh
{
    public const int PerRun = 40;

    /// <summary>Four fifths of the month's allowance; the rest is kept for the screens.</summary>
    public const int BackgroundCeiling = LookupMonth.Allowance * 4 / 5;

    public static IReadOnlyList<TitleId> Tracked(IEnumerable<WatchedSitting> sittings, IEnumerable<HouseholdPlaylist> playlists)
    {
        var shows = sittings
            .Where(sitting => sitting.Sitting.Kind == WatchedKind.Show)
            .Select(sitting => sitting.Sitting.Title);
        var listed = playlists
            .SelectMany(playlist => playlist.List.Entries)
            .Where(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)
            .Select(entry => new TitleId(entry.Key));
        return shows.Concat(listed).Distinct().ToList();
    }

    public static IReadOnlyList<TitleId> Due(IEnumerable<(TitleId Id, CacheStamp? Stamp)> tracked, int usedThisMonth, DateTimeOffset now)
    {
        var room = Math.Min(PerRun, BackgroundCeiling - usedThisMonth);
        if (room <= 0)
        {
            return [];
        }

        return tracked
            .Where(entry => CatalogueFreshness.NeedsFetch(entry.Stamp, tracked: true, opened: false, now))
            .OrderBy(entry => entry.Stamp?.FetchedAt ?? DateTimeOffset.MinValue)
            .Take(room)
            .Select(entry => entry.Id)
            .ToList();
    }
}
