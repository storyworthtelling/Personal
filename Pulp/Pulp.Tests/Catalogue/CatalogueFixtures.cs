using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Tests.Catalogue;

/// <summary>Invented titles and offers for the catalogue rules. Nothing here is a real title.</summary>
internal static class CatalogueFixtures
{
    public static TitleOffer Offer(
        string service,
        OfferKind kind,
        VideoQuality quality = VideoQuality.Hd,
        decimal? price = null,
        string? currency = null,
        bool known = true) =>
        new(new ServiceId(service), service, known, kind, quality, price, currency,
            "https://example.com/watch/" + service, null, null, false);

    public static CatalogueEpisode Episode(int season, int number, string name = "", int? airYear = null, params TitleOffer[] offers) =>
        new(new EpisodeRef(season, number), name, "An invented synopsis.", airYear, null, null, offers);

    public static CatalogueTitle Show(params CatalogueSeason[] seasons) => new()
    {
        Id = new TitleId("lantern-keepers"),
        Kind = TitleKind.Show,
        Name = "The Lantern Keepers",
        ImdbId = "tt9900001",
        Seasons = seasons,
    };

    public static AiredEpisode Aired(int season, int number, string date, string name = "Aired name") =>
        new(new EpisodeRef(season, number), name, DateOnly.Parse(date),
            new DateTimeOffset(DateOnly.Parse(date).ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero));
}
