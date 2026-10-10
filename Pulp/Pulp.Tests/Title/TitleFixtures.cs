using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Title;

/// <summary>Invented titles, services and a fixed clock for the title page's rules. No real title or service.</summary>
internal static class TitleFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 3, 12, 21, 0, 0, TimeSpan.Zero);

    public static readonly ServiceId Northstar = new("northstar-example");
    public static readonly ServiceId Lanternbox = new("lanternbox-example");
    public static readonly ServiceId Elsewhere = new("elsewhere-example");

    public static readonly TitleId Saltmarsh = new("saltmarsh-show-example");
    public static readonly TitleId Larkspur = new("larkspur-film-example");

    public static EpisodeRef E(int season, int number) => new(season, number);

    public static TitleOffer Offer(ServiceId service, VideoQuality quality, OfferKind kind = OfferKind.Subscription, string? link = null, string? video = null) =>
        new(service, service.Value, KnownService: true, kind, quality, Price: null, Currency: null,
            link ?? $"https://watch.example.com/{service.Value}/title", video, ExpiresOn: null, ExpiresSoon: false);

    /// <summary>Two seasons: six weekly episodes from a year ago, then eight announced, the first aired a month ago.</summary>
    public static CatalogueTitle Show(params TitleOffer[] offers) => new()
    {
        Id = Saltmarsh,
        Kind = TitleKind.Show,
        Name = "Saltmarsh",
        Genres = ["Mystery", "Drama"],
        Synopsis = "A keeper comes home to the marshes.",
        SeasonCount = 2,
        EpisodeCount = 14,
        Rating = ContentRating.Parse("TV-14"),
        Offers = offers,
        Seasons =
        [
            Season(1, 6, Now.AddDays(-365)),
            Season(2, 8, Now.AddDays(-30)),
        ],
    };

    public static CatalogueSeason Season(int number, int count, DateTimeOffset first) =>
        new(number, $"Season {number}", Enumerable.Range(1, count)
            .Select(n => new CatalogueEpisode(
                E(number, n), $"Episode {number}-{n}", $"What happens in {number}-{n}.", first.Year,
                DateOnly.FromDateTime(first.AddDays(7 * (n - 1)).UtcDateTime), first.AddDays(7 * (n - 1)), []))
            .ToList());

    public static CatalogueTitle Film(params TitleOffer[] offers) => new()
    {
        Id = Larkspur,
        Kind = TitleKind.Film,
        Name = "Larkspur",
        Genres = ["Thriller"],
        RuntimeMinutes = 112,
        Offers = offers,
    };

    public static WatchEvent Watched(EpisodeRef episode, DateTimeOffset at) =>
        new(Saltmarsh, WatchedKind.Show, at, Northstar, episode, Ended: true);
}
