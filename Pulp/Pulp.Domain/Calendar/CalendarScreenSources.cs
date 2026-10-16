using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// What the head reads before the calendar can be built: every adult's shows, every playlist the household
/// can see, the catalogue's copy of each title, and where a show plays at home. <paramref name="ShowsByAdult"/>
/// is every show the adult has sat with — not only those still on Continue watching, since a caught-up or
/// removed show's next episode still reaches the calendar (D21, D152).
/// </summary>
public sealed record CalendarScreenReads(
    IReadOnlyDictionary<ViewerId, IReadOnlyList<TitleId>> ShowsByAdult,
    IReadOnlyList<HouseholdPlaylist> Playlists,
    IReadOnlyDictionary<TitleId, CatalogueTitle> Titles,
    Func<CatalogueTitle, ServiceId?> HomeOf,
    TimeZoneInfo Zone,
    IReadOnlyList<FilmReleaseDate>? FilmDates = null,
    IReadOnlyList<HouseholdPlan>? Plans = null);

/// <summary>
/// The calendar's sources (<see cref="CalendarSources"/>) built from those reads, shared by the Calendar page
/// and the nightly writer so the page and the household calendar can never disagree about what is coming
/// (mockup 8: "the same events Pulp writes to the household calendar").
/// </summary>
public static class CalendarScreenSources
{
    /// <summary>
    /// An episode's service when neither the household's home nor any offer names one. The page leaves the
    /// service out of the line for it; the writer never prints a service.
    /// </summary>
    public static readonly ServiceId NoService = new("none");

    public static CalendarSources Build(CalendarScreenReads reads, IReadOnlyList<Fixture> games)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(games);

        var episodes = Episodes(reads);
        var plans = CalendarPlans.ToSources(reads.Plans ?? [], reads.ShowsByAdult.Keys.ToHashSet());
        return new CalendarSources(episodes, Releases(reads, episodes), games, plans);
    }

    private static List<EpisodeRelease> Episodes(CalendarScreenReads reads)
    {
        var watchers = new Dictionary<TitleId, List<ViewerId>>();
        foreach (var (adult, shows) in reads.ShowsByAdult)
        {
            foreach (var show in shows.Distinct())
            {
                if (!watchers.TryGetValue(show, out var list))
                {
                    list = [];
                    watchers[show] = list;
                }

                list.Add(adult);
            }
        }

        var episodes = new List<EpisodeRelease>();
        foreach (var (show, who) in watchers)
        {
            if (!reads.Titles.TryGetValue(show, out var title) || title.Kind != TitleKind.Show)
            {
                continue;
            }

            var service = ServiceOf(reads, title);
            foreach (var episode in Dated(title, reads.Zone))
            {
                episodes.Add(new EpisodeRelease(
                    title.Id, title.Name, episode.Episode.Episode.Season, episode.Episode.Episode.Number,
                    episode.Date, null, service, who));
            }
        }

        return episodes;
    }

    /// <summary>
    /// A show on a playlist releases when a season opens: its first episode. A film has no release date in
    /// the catalogue, so it releases only on a day the availability source announced (D302); none is made up. A premiere of a show the shelf's owners watch is already
    /// their new episode, and is not said twice.
    /// </summary>
    private static List<TitleRelease> Releases(CalendarScreenReads reads, IReadOnlyList<EpisodeRelease> episodes)
    {
        var releases = new List<TitleRelease>();
        var shows = reads.Playlists
            .SelectMany(playlist => playlist.List.Entries
                .Where(entry => entry.Kind == PlaylistEntryKind.Show)
                .Select(entry => (Title: new TitleId(entry.Key), Shelf: playlist.Shelf)))
            .GroupBy(pair => pair.Title);

        foreach (var group in shows)
        {
            if (!reads.Titles.TryGetValue(group.Key, out var title) || title.Kind != TitleKind.Show)
            {
                continue;
            }

            var shelves = group.Select(pair => pair.Shelf).Distinct().ToList();
            var service = ServiceOf(reads, title);
            foreach (var premiere in Dated(title, reads.Zone).Where(episode => episode.Episode.Episode.Number == 1))
            {
                var watching = episodes
                    .Where(episode => episode.Show == title.Id && episode.Date == premiere.Date
                        && episode.Season == premiere.Episode.Episode.Season)
                    .SelectMany(episode => episode.Watchers)
                    .ToHashSet();
                if (shelves.All(shelf => watching.Contains(shelf.Owner)))
                {
                    continue;
                }

                releases.Add(new TitleRelease(title.Id, title.Name, TitleKind.Show, premiere.Date, null, service, shelves));
            }
        }

        foreach (var film in reads.Playlists
            .SelectMany(playlist => playlist.List.Entries
                .Where(entry => entry.Kind == PlaylistEntryKind.Film)
                .Select(entry => (Title: new TitleId(entry.Key), Shelf: playlist.Shelf)))
            .GroupBy(pair => pair.Title))
        {
            var known = (reads.FilmDates ?? []).Where(date => date.Title == film.Key).OrderBy(date => date.Date).FirstOrDefault();
            if (known is null || !reads.Titles.TryGetValue(film.Key, out var title) || title.Kind != TitleKind.Film)
            {
                continue;
            }

            releases.Add(new TitleRelease(
                title.Id, title.Name, TitleKind.Film, known.Date, null, known.Service,
                film.Select(pair => pair.Shelf).Distinct().ToList()));
        }

        return releases;
    }

    private static ServiceId ServiceOf(CalendarScreenReads reads, CatalogueTitle title) =>
        reads.HomeOf(title)
        ?? title.Offers.Where(offer => offer.KnownService).Select(offer => offer.Service).Cast<ServiceId?>().FirstOrDefault()
        ?? NoService;

    /// <summary>
    /// The show's episodes with a day: the source's own date when it gives one (a date, not an instant that
    /// a zone could slide), else the day the instant falls on in the viewer's zone.
    /// </summary>
    private static IEnumerable<(CatalogueEpisode Episode, DateOnly Date)> Dated(CatalogueTitle title, TimeZoneInfo zone)
    {
        foreach (var episode in title.Seasons.SelectMany(season => season.Episodes))
        {
            if (episode.Episode.Season <= 0 || episode.Episode.Number <= 0)
            {
                continue;
            }

            if (episode.AirDate is { } date)
            {
                yield return (episode, date);
            }
            else if (episode.AirsAt is { } instant)
            {
                yield return (episode, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime));
            }
        }
    }
}
