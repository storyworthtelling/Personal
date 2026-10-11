using Pulp.Domain.Library;

namespace Pulp.Domain.Catalogue;

/// <summary>
/// One episode. The availability source gives the name, synopsis, air year and offers but no air
/// date; <see cref="AirDate"/> and <see cref="AirsAt"/> come from the episode source
/// (<see cref="EpisodeAirDates.Merge"/>). An episode the episode source announced before any service
/// carried it has no offers yet.
/// </summary>
public sealed record CatalogueEpisode(
    EpisodeRef Episode,
    string Name,
    string Synopsis,
    int? AirYear,
    DateOnly? AirDate,
    DateTimeOffset? AirsAt,
    IReadOnlyList<TitleOffer> Offers);
