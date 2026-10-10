using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Domain.Discover;

/// <summary>
/// Whose stack the phone shows (D23: the device owner's), and what that viewer has watched (D282) and
/// learned from (D283). Nothing in the household yet says which adult holds this phone — first run is
/// Release D's — so <see cref="Me"/> answers only when the roster leaves no doubt: exactly one adult.
/// With two it answers nobody, and the page says so, rather than deal one adult's stack by the other's
/// taste.
/// </summary>
public static class DiscoverPageViewer
{
    public static ViewerId? Me(HouseholdRoster? roster)
    {
        if (roster is null)
        {
            return null;
        }

        var adults = roster.Viewers.Where(viewer => !viewer.IsChild).ToList();
        return adults.Count == 1 ? adults[0].Id : null;
    }

    /// <summary>
    /// The titles the viewer has watched, by the one watched rule (<see cref="Sittings.Verdict"/>): a
    /// film whose own sittings say watched, a show with any episode watched — a show you have started
    /// is not something new (D282).
    /// </summary>
    public static IReadOnlySet<TitleId> AlreadyWatched(IEnumerable<WatchEvent> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);
        return sittings
            .GroupBy(sitting => sitting.Title)
            .Where(title => IsWatched(title))
            .Select(title => title.Key)
            .ToHashSet();
    }

    /// <summary>
    /// What <see cref="TasteLearner.Learn"/> learns from: each title anyone watched, with every viewer
    /// who watched it, so a title a child watched too is a child's viewing and never counts (D23, D283).
    /// </summary>
    public static IReadOnlyList<WatchedTitle> WatchedTitles(
        IReadOnlyDictionary<ViewerId, IReadOnlyList<WatchEvent>> sittings,
        Func<TitleId, IReadOnlyList<string>> tagsOf)
    {
        ArgumentNullException.ThrowIfNull(sittings);
        ArgumentNullException.ThrowIfNull(tagsOf);

        var viewersOf = new Dictionary<TitleId, List<ViewerId>>();
        foreach (var (viewer, theirs) in sittings)
        {
            foreach (var title in AlreadyWatched(theirs))
            {
                if (!viewersOf.TryGetValue(title, out var viewers))
                {
                    viewersOf[title] = viewers = [];
                }

                viewers.Add(viewer);
            }
        }

        return viewersOf
            .OrderBy(entry => entry.Key.Value, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new WatchedTitle(entry.Key, tagsOf(entry.Key) ?? [], entry.Value))
            .ToList();
    }

    private static bool IsWatched(IGrouping<TitleId, WatchEvent> title)
    {
        var shows = title.Where(sitting => sitting.Episode is not null).GroupBy(sitting => sitting.Episode);
        if (shows.Any(episode => Sittings.Verdict(episode).IsWatched))
        {
            return true;
        }

        var whole = title.Where(sitting => sitting.Episode is null).ToList();
        return whole.Count > 0 && Sittings.Verdict(whole).IsWatched;
    }
}
