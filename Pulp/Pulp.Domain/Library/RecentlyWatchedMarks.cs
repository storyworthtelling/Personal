using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>
/// What a long-press on a Recently watched row writes (mockup 05, D160, D278). "Mark unwatched" is a
/// mark by hand, never a sitting — <see cref="RecentlyWatched"/> keeps the row under the day it was
/// last watched: a film takes one whole-title mark; a show takes one per episode
/// <see cref="Sittings.Verdict"/> calls watched, so each episode's own verdict turns (the mark lasts
/// until newer evidence, D278). "Remove from history" is every one of the viewer's records of the
/// title, marks included, so nothing of it is left to rebuild a row from.
/// </summary>
public static class RecentlyWatchedMarks
{
    /// <param name="events">The viewer's own sittings (D97); only <paramref name="title"/>'s are read.</param>
    public static IReadOnlyList<WatchEvent> Unwatch(IEnumerable<WatchEvent> events, TitleId title, DateTimeOffset at)
    {
        var history = History(events, title);
        if (history.Count == 0)
        {
            return [];
        }

        var kind = history.OrderBy(sitting => sitting.At).Last().Kind;
        if (kind != WatchedKind.Show)
        {
            return [new WatchEvent(title, kind, at, Service: null, Evidence: WatchSource.MarkedUnwatched)];
        }

        return history
            .Where(sitting => sitting.Episode is not null)
            .GroupBy(sitting => sitting.Episode!)
            .Where(episode => Sittings.Verdict(episode).IsWatched)
            .OrderBy(episode => episode.Key)
            .Select(episode => new WatchEvent(title, WatchedKind.Show, at, Service: null, episode.Key, WatchSource.MarkedUnwatched))
            .ToList();
    }

    public static IReadOnlyList<WatchEvent> History(IEnumerable<WatchEvent> events, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Where(sitting => sitting.Title == title).ToList();
    }
}
