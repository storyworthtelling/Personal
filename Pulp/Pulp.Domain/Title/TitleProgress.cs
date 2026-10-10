using Pulp.Domain.Actions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Domain.Title;

/// <summary>
/// One viewer's place in a title, as the title page shows it (D159, D217): which episodes take the
/// pine tick, which one is next, and which season the accordion opens on. "Watched" is
/// <see cref="Sittings.Verdict"/>'s answer per episode and "next" is <see cref="ContinueWatching"/>'s,
/// so the page and the home can never disagree about where the viewer is.
/// </summary>
public static class TitleProgress
{
    /// <summary>The episodes of <paramref name="title"/> the watched rule says are watched.</summary>
    public static IReadOnlySet<EpisodeRef> WatchedEpisodes(TitleId title, IEnumerable<WatchEvent> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);
        return sittings
            .Where(sitting => sitting.Title.Equals(title) && sitting.Episode is not null)
            .GroupBy(sitting => sitting.Episode!)
            .Where(episode => Sittings.Verdict(episode).IsWatched)
            .Select(episode => episode.Key)
            .ToHashSet();
    }

    /// <summary>
    /// The episode the viewer is up to, or null when nothing aired is left (or for a film). With a
    /// schedule and sittings it is Continue Watching's next-up; a show never started begins at its
    /// first aired episode; a show whose dates are unknown takes the first episode after the furthest
    /// one watched.
    /// </summary>
    public static EpisodeRef? NextUp(CatalogueTitle title, IEnumerable<WatchEvent> sittings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(sittings);
        if (title.Kind != TitleKind.Show)
        {
            return null;
        }

        var own = sittings.Where(sitting => sitting.Title.Equals(title.Id)).ToList();
        if (own.Count > 0)
        {
            var row = ContinueWatching.Build(own, [CatalogueSchedule.For(title)], now).FirstOrDefault();
            if (row?.NextUp is { } next)
            {
                return next;
            }
        }

        var watched = WatchedEpisodes(title.Id, own);
        var furthest = watched.Max();
        return Episodes(title)
            .Where(episode => Aired(episode, now))
            .Select(episode => episode.Episode)
            .Where(episode => !watched.Contains(episode) && (furthest is null || episode.CompareTo(furthest) > 0))
            .Order()
            .FirstOrDefault();
    }

    /// <summary>What Play plays: the next episode, or — with nothing left — the show from its first episode. Null for a film.</summary>
    public static EpisodeRef? ToPlay(CatalogueTitle title, IEnumerable<WatchEvent> sittings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Kind != TitleKind.Show)
        {
            return null;
        }

        return NextUp(title, sittings, now) ?? Episodes(title).Select(episode => episode.Episode).Order().FirstOrDefault();
    }

    /// <summary>The season the accordion opens on (D159): the next episode's, else the last one watched in, else the first.</summary>
    public static int OpenSeason(CatalogueTitle show, EpisodeRef? next, IReadOnlySet<EpisodeRef> watched)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(watched);
        if (next is not null)
        {
            return next.Season;
        }

        if (watched.Count > 0)
        {
            return watched.Max()!.Season;
        }

        return show.Seasons.Count > 0 ? show.Seasons.Min(season => season.Number) : 1;
    }

    internal static IEnumerable<CatalogueEpisode> Episodes(CatalogueTitle title) =>
        title.Seasons.SelectMany(season => season.Episodes);

    // An episode with no date at all is listed by the source as available, so it counts as aired.
    private static bool Aired(CatalogueEpisode episode, DateTimeOffset now)
    {
        if (episode.AirsAt is { } at)
        {
            return at <= now;
        }

        return episode.AirDate is not { } date || date <= DateOnly.FromDateTime(now.UtcDateTime);
    }
}

/// <summary>
/// What a long-press mark on an episode writes (D160, D278): one manual mark per episode, so the
/// latest mark wins over what came before and anything watched after it counts again. "Watched up to
/// here" marks every earlier episode not already watched, and the pressed one — never re-marking one
/// already watched, so its undo removes only what this mark added.
/// </summary>
public static class EpisodeMarks
{
    public static IReadOnlyList<WatchEvent> For(
        CardActionKind kind, CatalogueTitle show, EpisodeRef target, IReadOnlySet<EpisodeRef> watched, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(watched);

        var listed = TitleProgress.Episodes(show).Select(episode => episode.Episode).ToList();
        if (show.Kind != TitleKind.Show || !listed.Contains(target))
        {
            throw new ArgumentException(
                $"'{show.Id}' lists no S{target.Season} E{target.Number}; a mark on an episode the page never showed would mark nothing anyone can see.",
                nameof(target));
        }

        return kind switch
        {
            CardActionKind.Watched => [Mark(show.Id, target, WatchSource.MarkedWatched, at)],
            CardActionKind.Unwatched => [Mark(show.Id, target, WatchSource.MarkedUnwatched, at)],
            CardActionKind.WatchedUpToHere => listed
                .Where(episode => episode.CompareTo(target) <= 0 && (episode == target || !watched.Contains(episode)))
                .Order()
                .Select(episode => Mark(show.Id, episode, WatchSource.MarkedWatched, at))
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An episode's card offers only Watched, Unwatched and Watched up to here (D160)."),
        };
    }

    private static WatchEvent Mark(TitleId show, EpisodeRef episode, WatchSource evidence, DateTimeOffset at) =>
        new(show, WatchedKind.Show, at, Service: null, episode, evidence);
}

/// <summary>
/// Whose page this is. Pulp has no "who is holding the phone" yet (first run is Release D's), so the
/// title page takes the roster's first adult — never a child, since a hand-off from this page is not
/// put through a child's view, and never a guess when there is no adult at all.
/// </summary>
public static class TitleViewer
{
    public static ViewerId? Of(HouseholdRoster? roster) =>
        roster?.Viewers.FirstOrDefault(viewer => !viewer.IsChild)?.Id;
}
