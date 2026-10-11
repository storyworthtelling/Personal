using Pulp.Domain.Calendar;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>
/// Where the page's and the nightly writer's sources come from (D146, D21): every show an adult has sat
/// with — a caught-up or removed show's next episode still reaches the calendar — a playlist show's season
/// premiere, and the followed games. A film on a playlist has no date in the catalogue, so it cannot be on
/// the calendar, and nothing is invented for it. Invented titles throughout.
/// </summary>
public class CalendarScreenSourcesTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");
    private static readonly ServiceId Northstar = new("northstar-plus");
    private static readonly ServiceId Streamly = new("streamly");

    private static CatalogueEpisode Aired(int season, int number, DateOnly? date, DateTimeOffset? at = null) =>
        new(new EpisodeRef(season, number), "E" + number, string.Empty, date?.Year, date, at, []);

    private static CatalogueTitle Show(string id, params CatalogueEpisode[] episodes) => new()
    {
        Id = new TitleId(id),
        Kind = TitleKind.Show,
        Name = id,
        Seasons = episodes.GroupBy(episode => episode.Episode.Season)
            .Select(season => new CatalogueSeason(season.Key, "Season " + season.Key, season.ToList()))
            .ToList(),
        Offers = [Offer(Streamly)],
    };

    private static CatalogueTitle Film(string id) => new() { Id = new TitleId(id), Kind = TitleKind.Film, Name = id };

    private static TitleOffer Offer(ServiceId service) =>
        new(service, service.Value, true, OfferKind.Subscription, Pulp.Domain.Availability.VideoQuality.Hd, null, null, "https://example.com", null, null, false);

    private static HouseholdPlaylist Playlist(string id, ViewerId owner, bool shared, string name, params PlaylistEntry[] entries)
    {
        var list = ListOf(name, entries);
        return new HouseholdPlaylist(new PlaylistId(id), owner, shared, list);
    }

    private static Pulp.Domain.Library.Playlist ListOf(string name, IEnumerable<PlaylistEntry> entries) =>
        entries.Aggregate(Pulp.Domain.Library.Playlist.Named(name), (list, entry) => list.Add(entry));

    private static CalendarScreenReads Reads(
        Dictionary<ViewerId, IReadOnlyList<TitleId>>? shows = null,
        IReadOnlyList<HouseholdPlaylist>? playlists = null,
        params CatalogueTitle[] titles) => new(
        shows ?? [],
        playlists ?? [],
        titles.ToDictionary(title => title.Id),
        title => title.Id.Value == "saltmarsh" ? Northstar : null,
        Zone);

    [Fact]
    public void AShowAnAdultHasSatWithBecomesItsDatedEpisodesWithBothWatchers()
    {
        var saltmarsh = Show("saltmarsh", Aired(1, 4, new DateOnly(2026, 10, 9)), Aired(1, 5, new DateOnly(2026, 10, 16)));
        var reads = Reads(
            new() { [Sam] = [saltmarsh.Id], [Alex] = [saltmarsh.Id] },
            null,
            saltmarsh);

        var sources = CalendarScreenSources.Build(reads, []);

        Assert.Equal([4, 5], sources.Episodes.Select(episode => episode.Episode));
        Assert.All(sources.Episodes, episode => Assert.Equal([Alex, Sam], episode.Watchers.OrderBy(who => who.Value)));
        Assert.All(sources.Episodes, episode => Assert.Equal(Northstar, episode.Service));
        Assert.All(sources.Episodes, episode => Assert.Null(episode.LivePremiere));
    }

    [Fact]
    public void TheEpisodesDateIsTheSourcesDateNotAZoneShiftedInstant()
    {
        // 02:00 UTC on the 10th is the evening of the 9th five hours behind; the source says the 10th.
        var show = Show("saltmarsh", Aired(1, 4, new DateOnly(2026, 10, 10), new DateTimeOffset(2026, 10, 10, 2, 0, 0, TimeSpan.Zero)));
        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [show.Id] }, null, show), []);

        Assert.Equal(new DateOnly(2026, 10, 10), sources.Episodes.Single().Date);
    }

    [Fact]
    public void AnEpisodeWithOnlyAnInstantIsDatedInTheViewersZone()
    {
        var show = Show("saltmarsh", Aired(1, 4, null, new DateTimeOffset(2026, 10, 10, 2, 0, 0, TimeSpan.Zero)));
        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [show.Id] }, null, show), []);

        Assert.Equal(new DateOnly(2026, 10, 9), sources.Episodes.Single().Date);
    }

    [Fact]
    public void AnEpisodeWithNoDateAtAllIsLeftOff()
    {
        var show = Show("saltmarsh", Aired(1, 4, null));
        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [show.Id] }, null, show), []);

        Assert.Empty(sources.Episodes);
    }

    [Fact]
    public void AShowWithNoKnownServiceStillGetsItsEpisodeUnderTheNoServiceMarker()
    {
        var show = Show("quarry-road", Aired(1, 1, new DateOnly(2026, 10, 9))) with { Offers = [] };
        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [show.Id] }, null, show), []);

        Assert.Equal(CalendarScreenSources.NoService, sources.Episodes.Single().Service);
    }

    [Fact]
    public void AShowTheCatalogueHasNoCopyOfIsSkippedNotInvented()
    {
        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [new TitleId("never-fetched")] }), []);

        Assert.Empty(sources.Episodes);
    }

    [Fact]
    public void APlaylistShowReleasesOnItsSeasonPremiereWithTheShelves()
    {
        var show = Show("tidewater", Aired(2, 1, new DateOnly(2026, 10, 14)), Aired(2, 2, new DateOnly(2026, 10, 21)));
        var shelf = Playlist("p-1", Sam, true, "Rainy Sundays", PlaylistEntry.ForTitle(show.Id, TitleKind.Show));

        var sources = CalendarScreenSources.Build(Reads(null, [shelf], show), []);

        var release = Assert.Single(sources.Releases);
        Assert.Equal(new DateOnly(2026, 10, 14), release.Date);
        Assert.Equal(show.Id, release.Title);
        Assert.Equal(new PlaylistShelf("Rainy Sundays", Sam, true), Assert.Single(release.Playlists));
    }

    [Fact]
    public void AFilmOnAPlaylistHasNoReleaseDateSoItIsNotOnTheCalendar()
    {
        var film = Film("high-pass");
        var shelf = Playlist("p-1", Sam, false, "Rainy Sundays", PlaylistEntry.ForTitle(film.Id, TitleKind.Film));

        var sources = CalendarScreenSources.Build(Reads(null, [shelf], film), []);

        Assert.Empty(sources.Releases);
    }

    [Fact]
    public void ASeasonPremiereOfAShowItsOwnerWatchesIsTheEpisodeNotAlsoARelease()
    {
        var show = Show("tidewater", Aired(2, 1, new DateOnly(2026, 10, 14)));
        var shelf = Playlist("p-1", Sam, false, "Rainy Sundays", PlaylistEntry.ForTitle(show.Id, TitleKind.Show));

        var sources = CalendarScreenSources.Build(Reads(new() { [Sam] = [show.Id] }, [shelf], show), []);

        Assert.Single(sources.Episodes);
        Assert.Empty(sources.Releases);
    }

    [Fact]
    public void TheGamesPassThroughAndThereAreNoPlansYet()
    {
        var game = new Fixture(
            new GameId("g-1"), new LeagueId("cfl-invented"), "Harbor Gulls at Ridge Foxes",
            new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero), GameStage.RegularSeason, "Gridline TV", [Sam], FixtureKind.Game);

        var sources = CalendarScreenSources.Build(Reads(), [game]);

        Assert.Equal([game], sources.Games);
        Assert.Empty(sources.Plans);
    }
}
