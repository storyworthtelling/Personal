using Pulp.Domain.Core;
using Pulp.Domain.Ratings;

namespace Pulp.Domain.Catalogue;

/// <summary>
/// One title as the screens read it: what the availability source says about it (name, year,
/// genres, synopsis, poster, length, offers, seasons), the rating chip's code from the rating source
/// (D207: the code only, never reasons), and — for a show — the air dates and ended flag the episode
/// source adds. Built by Infrastructure's clients; the rules that read it live beside it.
/// </summary>
public sealed record CatalogueTitle
{
    public required TitleId Id { get; init; }

    public required TitleKind Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>The IMDb id, which is how the rating and episode sources are asked about this title.</summary>
    public string? ImdbId { get; init; }

    /// <summary>A film's release year, or the year a show first aired.</summary>
    public int? Year { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string Synopsis { get; init; } = string.Empty;

    public string? PosterUrl { get; init; }

    /// <summary>A film's runtime in minutes; null for a show.</summary>
    public int? RuntimeMinutes { get; init; }

    /// <summary>A show's season count, aired or announced; null for a film.</summary>
    public int? SeasonCount { get; init; }

    /// <summary>A show's episode count, aired or announced; null for a film.</summary>
    public int? EpisodeCount { get; init; }

    public ContentRating Rating { get; init; } = ContentRating.Unrated;

    /// <summary>Every way to watch the whole title in the household's country, on any service.</summary>
    public IReadOnlyList<TitleOffer> Offers { get; init; } = [];

    /// <summary>A show's seasons, in order; empty for a film or when the source was asked without them.</summary>
    public IReadOnlyList<CatalogueSeason> Seasons { get; init; } = [];

    /// <summary>Whether the show has ended, from the episode source; null until it has been asked.</summary>
    public bool? Ended { get; init; }
}

/// <summary>A season of a show, numbered from one, with its episodes in order.</summary>
public sealed record CatalogueSeason(int Number, string Name, IReadOnlyList<CatalogueEpisode> Episodes);
