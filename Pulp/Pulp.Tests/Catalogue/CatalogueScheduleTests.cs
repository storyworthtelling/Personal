using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Catalogue.CatalogueFixtures;

namespace Pulp.Tests.Catalogue;

public class CatalogueScheduleTests
{
    [Fact]
    public void DatedEpisodesBecomeTheScheduleContinueWatchingReads()
    {
        var show = EpisodeAirDates.Merge(
            Show(new CatalogueSeason(1, "Season 1", [Episode(1, 1), Episode(1, 2), Episode(1, 3)])),
            new ShowAirDates(true, [Aired(1, 1, "2024-01-02"), Aired(1, 2, "2024-01-09")]));

        var schedule = CatalogueSchedule.For(show);

        Assert.Equal(new TitleId("lantern-keepers"), schedule.Title);
        Assert.True(schedule.Ended);
        // Episode 3 has no date: it cannot be said to have come out, so it is not scheduled.
        Assert.Equal([new EpisodeRef(1, 1), new EpisodeRef(1, 2)], schedule.Episodes.Select(e => e.Episode));
    }

    [Fact]
    public void ADateWithNoTimeIsTheStartOfThatDayInUtc()
    {
        var show = Show(new CatalogueSeason(1, "Season 1",
            [Episode(1, 1) with { AirDate = new DateOnly(2024, 5, 6), AirsAt = null }]));

        var scheduled = Assert.Single(CatalogueSchedule.For(show).Episodes);

        Assert.Equal(new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero), scheduled.AirsAt);
    }

    [Fact]
    public void TheNextEpisodeIsPickedByContinueWatchingFromTheMergedSchedule()
    {
        var show = EpisodeAirDates.Merge(
            Show(new CatalogueSeason(1, "Season 1", [Episode(1, 1), Episode(1, 2)])),
            new ShowAirDates(false, [Aired(1, 1, "2024-01-02"), Aired(1, 2, "2024-01-09"), Aired(1, 3, "2030-01-01")]));
        var now = new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero);
        var sitting = new WatchEvent(show.Id, WatchedKind.Show, now.AddDays(-20), null, new EpisodeRef(1, 1), WatchSource.MarkedWatched);

        var row = Assert.Single(ContinueWatching.Build([sitting], [CatalogueSchedule.For(show)], now));

        Assert.Equal(new EpisodeRef(1, 2), row.NextUp);
    }

    [Fact]
    public void AFilmHasNoSchedule()
    {
        var film = Show() with { Kind = TitleKind.Film };

        Assert.Throws<ArgumentException>(() => CatalogueSchedule.For(film));
    }
}
