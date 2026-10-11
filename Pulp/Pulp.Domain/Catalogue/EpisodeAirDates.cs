using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Catalogue;

/// <summary>An episode as the episode source lists it: its number, name and when it airs.</summary>
public sealed record AiredEpisode(EpisodeRef Episode, string Name, DateOnly? AirDate, DateTimeOffset? AirsAt);

/// <summary>A show's episodes from the episode source, and whether the show has ended.</summary>
public sealed record ShowAirDates(bool Ended, IReadOnlyList<AiredEpisode> Episodes);

/// <summary>
/// Puts the episode source's air dates onto the availability source's episodes (D21, D146). The
/// availability source numbers nothing and dates nothing finer than a year; the episode source
/// dates everything, including episodes announced before any service carries them. The merge keeps
/// the availability source's words and offers, takes the dates by season and number, and adds the
/// announced episodes it did not list, with no offers.
/// </summary>
public static class EpisodeAirDates
{
    public static CatalogueTitle Merge(CatalogueTitle title, ShowAirDates airDates)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(airDates);
        if (title.Kind != TitleKind.Show)
        {
            throw new ArgumentException($"'{title.Id}' is a film; only a show has episodes to date.", nameof(title));
        }

        // First listing of an episode wins, so a source repeating one cannot reorder the season.
        var aired = new Dictionary<EpisodeRef, AiredEpisode>();
        foreach (var episode in airDates.Episodes.Where(e => e.Episode.Season > 0 && e.Episode.Number > 0))
        {
            aired.TryAdd(episode.Episode, episode);
        }

        var seasons = new SortedDictionary<int, (string Name, List<CatalogueEpisode> Episodes)>();
        foreach (var season in title.Seasons)
        {
            var episodes = season.Episodes.Select(episode => Dated(episode, aired.GetValueOrDefault(episode.Episode))).ToList();
            seasons[season.Number] = (season.Name, episodes);
        }

        var listed = title.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.Episode).ToHashSet();
        foreach (var announced in aired.Values.Where(episode => !listed.Contains(episode.Episode)))
        {
            var number = announced.Episode.Season;
            if (!seasons.TryGetValue(number, out var season))
            {
                season = ("Season " + number, []);
                seasons[number] = season;
            }

            season.Episodes.Add(new CatalogueEpisode(
                announced.Episode, announced.Name, string.Empty, announced.AirDate?.Year, announced.AirDate, announced.AirsAt, []));
        }

        return title with
        {
            Ended = airDates.Ended,
            Seasons = seasons
                .Select(pair => new CatalogueSeason(pair.Key, pair.Value.Name, pair.Value.Episodes.OrderBy(e => e.Episode).ToList()))
                .ToList(),
        };
    }

    private static CatalogueEpisode Dated(CatalogueEpisode episode, AiredEpisode? aired) =>
        aired is null
            ? episode
            : episode with
            {
                Name = string.IsNullOrWhiteSpace(episode.Name) ? aired.Name : episode.Name,
                AirDate = aired.AirDate ?? episode.AirDate,
                AirsAt = aired.AirsAt ?? episode.AirsAt,
            };
}
