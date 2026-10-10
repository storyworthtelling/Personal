using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Domain.Discover;

/// <summary>
/// What the stack's viewer has watched (D282) and what taste learns from (D283). Whose stack it is
/// (D23: the device owner's) is <see cref="People.ThisPhone"/>'s one answer, not this page's.
/// </summary>
public static class DiscoverPageWatched
{
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
