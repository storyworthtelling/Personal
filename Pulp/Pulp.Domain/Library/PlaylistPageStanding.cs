using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>
/// How one playlist entry stands for one viewer — the <see cref="EntryStanding"/> that
/// <see cref="Playlists.Rows"/> strikes and dims by. The viewer's own sittings of that title are the
/// evidence, and every "is it watched" is <see cref="Sittings.Verdict"/>'s answer (D278, the one rule):
/// <list type="bullet">
/// <item>a film (or a game) is watched when the verdict says so (D101);</item>
/// <item>a show is struck only when it has ended and every episode is watched — finished for good (D153);</item>
/// <item>a show with nothing aired left to watch is caught up (D224), and its next season and the date
/// it lands are named when the next episode to air opens a season; one with episodes left is never
/// dimmed by what is coming.</item>
/// </list>
/// A show whose schedule is not known, a franchise, or anything never watched stands
/// <see cref="EntryStanding.Unknown"/> — plain, never guessed at. A film's release date is not in the
/// catalogue model, so a film is never dimmed as not out yet from here (see the brief's report).
/// </summary>
public static class PlaylistPageStanding
{
    /// <param name="sittings">The viewer's own sittings of this entry's title (D97).</param>
    /// <param name="title">The catalogue's copy, for a show's schedule; null when not looked up.</param>
    public static EntryStanding For(PlaylistEntry entry, IReadOnlyList<WatchEvent> sittings, CatalogueTitle? title, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sittings);
        ArgumentNullException.ThrowIfNull(zone);

        return entry.Kind switch
        {
            PlaylistEntryKind.Film or PlaylistEntryKind.Game => Whole(sittings),
            PlaylistEntryKind.Show => title is { Kind: TitleKind.Show } show ? Show(sittings, show, now, zone) : EntryStanding.Unknown,
            PlaylistEntryKind.Franchise => EntryStanding.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "A playlist entry kind with no standing decided."),
        };
    }

    private static EntryStanding Whole(IReadOnlyList<WatchEvent> sittings) =>
        sittings.Count > 0 && Sittings.Verdict(sittings).IsWatched ? new EntryStanding(Watched: true) : EntryStanding.Unknown;

    private static EntryStanding Show(IReadOnlyList<WatchEvent> sittings, CatalogueTitle show, DateTimeOffset now, TimeZoneInfo zone)
    {
        var schedule = CatalogueSchedule.For(show);
        if (schedule.Episodes.Count == 0)
        {
            return EntryStanding.Unknown;
        }

        var watched = ContinueWatching.WatchedEpisodes(sittings.OrderBy(sitting => sitting.At));
        if (watched.Count == 0)
        {
            return EntryStanding.Unknown;
        }

        // The same "left to watch" Continue Watching reads (D152): aired, unwatched, past the furthest seen.
        var furthest = watched.Max()!;
        var left = schedule.Episodes
            .Where(episode => episode.AirsAt <= now)
            .Any(episode => !watched.Contains(episode.Episode) && episode.Episode.CompareTo(furthest) > 0);
        if (left)
        {
            return EntryStanding.Unknown;
        }

        var next = schedule.Episodes.Where(episode => episode.AirsAt > now).OrderBy(episode => episode.Episode).FirstOrDefault();
        if (next is null && schedule.Ended)
        {
            return new EntryStanding(Watched: true);
        }

        var opensSeason = next is not null && next.Episode.Number == 1 && next.Episode.Season > furthest.Season;
        return new EntryStanding(
            CaughtUp: true,
            NextSeason: opensSeason ? next!.Episode.Season : null,
            NextSeasonOut: opensSeason ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next!.AirsAt, zone).DateTime) : null);
    }
}
