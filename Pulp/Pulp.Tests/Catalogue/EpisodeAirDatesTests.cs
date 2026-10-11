using Pulp.Domain.Catalogue;
using Pulp.Domain.Library;
using static Pulp.Tests.Catalogue.CatalogueFixtures;

namespace Pulp.Tests.Catalogue;

public class EpisodeAirDatesTests
{
    [Fact]
    public void AirDatesLandOnTheMatchingSeasonAndNumber()
    {
        var show = Show(
            new CatalogueSeason(1, "Season 1", [Episode(1, 1, "Lamplight"), Episode(1, 2, "Tidewater")]),
            new CatalogueSeason(2, "Season 2", [Episode(2, 1, "Low Bridge")]));

        var merged = EpisodeAirDates.Merge(show, new ShowAirDates(false,
        [
            Aired(2, 1, "2025-03-04"),
            Aired(1, 2, "2024-01-09"),
            Aired(1, 1, "2024-01-02"),
        ]));

        Assert.Equal(DateOnly.Parse("2024-01-02"), merged.Seasons[0].Episodes[0].AirDate);
        Assert.Equal(DateOnly.Parse("2024-01-09"), merged.Seasons[0].Episodes[1].AirDate);
        Assert.Equal(DateOnly.Parse("2025-03-04"), merged.Seasons[1].Episodes[0].AirDate);
        Assert.NotNull(merged.Seasons[1].Episodes[0].AirsAt);
        Assert.False(merged.Ended);
    }

    [Fact]
    public void TheAvailabilitySourcesWordsAndOffersAreKept()
    {
        var offer = Offer("northstar", OfferKind.Subscription);
        var show = Show(new CatalogueSeason(1, "Season 1", [Episode(1, 1, "Lamplight", 2024, offer)]));

        var merged = EpisodeAirDates.Merge(show, new ShowAirDates(true, [Aired(1, 1, "2024-01-02", "A different name")]));

        var episode = Assert.Single(Assert.Single(merged.Seasons).Episodes);
        Assert.Equal("Lamplight", episode.Name);
        Assert.Equal("An invented synopsis.", episode.Synopsis);
        Assert.Equal([offer], episode.Offers);
        Assert.True(merged.Ended);
    }

    [Fact]
    public void AnEpisodeWithNoNameTakesTheEpisodeSourcesName()
    {
        var show = Show(new CatalogueSeason(1, "Season 1", [Episode(1, 1, name: "")]));

        var merged = EpisodeAirDates.Merge(show, new ShowAirDates(false, [Aired(1, 1, "2024-01-02", "Harbour Fog")]));

        Assert.Equal("Harbour Fog", merged.Seasons[0].Episodes[0].Name);
    }

    [Fact]
    public void AnnouncedEpisodesTheAvailabilitySourceDidNotListAreAddedWithNoOffers()
    {
        var show = Show(new CatalogueSeason(1, "Season 1", [Episode(1, 1, "Lamplight")]));

        var merged = EpisodeAirDates.Merge(show, new ShowAirDates(false,
        [
            Aired(1, 1, "2024-01-02"),
            Aired(2, 1, "2026-11-20", "Not Yet Lit"),
            Aired(1, 2, "2024-01-09", "Tidewater"),
        ]));

        Assert.Equal([1, 2], merged.Seasons.Select(season => season.Number));
        Assert.Equal([new EpisodeRef(1, 1), new EpisodeRef(1, 2)], merged.Seasons[0].Episodes.Select(e => e.Episode));
        var announced = Assert.Single(merged.Seasons[1].Episodes);
        Assert.Equal("Not Yet Lit", announced.Name);
        Assert.Empty(announced.Offers);
        Assert.Equal(DateOnly.Parse("2026-11-20"), announced.AirDate);
    }

    [Fact]
    public void AFilmIsRefused()
    {
        var film = Show() with { Kind = Pulp.Domain.Core.TitleKind.Film };

        Assert.Throws<ArgumentException>(() => EpisodeAirDates.Merge(film, new ShowAirDates(false, [])));
    }
}
