using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Title;
using static Pulp.Tests.Title.TitleFixtures;

namespace Pulp.Tests.Title;

/// <summary>
/// Where a title page says the title is, and which link Play hands off (D28, D29, D216). Invented
/// services only.
///
/// <para>The bugs these reintroduce: a page offering a service the household does not have, "open
/// there instead" that does not stick, an add-on or a rental counted as a home, Play sending the
/// show's link when the episode has its own (the app opens on the show, not the episode), and a
/// non-https link reaching the hand-off at all.</para>
/// </summary>
public class TitleHomesTests
{
    private static readonly ServiceId[] Ours = [Northstar, Lanternbox];

    [Fact]
    public void TheBestHomeIsTheBestPictureAndTheOtherSitsBeneathIt()
    {
        var show = Show(Offer(Lanternbox, VideoQuality.Hd, OfferKind.Free), Offer(Northstar, VideoQuality.Uhd));

        var choice = TitleHomes.Choose(show, Ours, HomeOverrides.None);

        Assert.Equal(new Home(Northstar, VideoQuality.Uhd, WithAds: false), choice.Best);
        Assert.Equal([new Home(Lanternbox, VideoQuality.Hd, WithAds: true)], choice.Others);
    }

    [Fact]
    public void OpenThereInsteadMakesTheOtherTheHome()
    {
        var show = Show(Offer(Lanternbox, VideoQuality.Hd, OfferKind.Free), Offer(Northstar, VideoQuality.Uhd));

        var choice = TitleHomes.Choose(show, Ours, HomeOverrides.None.OpenThereInstead(Saltmarsh, Lanternbox));

        Assert.Equal(Lanternbox, choice.Best!.Service);
        Assert.Equal([Northstar], choice.Others.Select(home => home.Service));
    }

    [Fact]
    public void AServiceTheHouseholdDoesNotHaveIsNoHome()
    {
        var show = Show(Offer(Elsewhere, VideoQuality.Uhd), Offer(Lanternbox, VideoQuality.Sd));

        var choice = TitleHomes.Choose(show, Ours, HomeOverrides.None);

        Assert.Equal(Lanternbox, choice.Best!.Service);
        Assert.Empty(choice.Others);
    }

    [Fact]
    public void RentalsAndAddOnsAreNotHomes()
    {
        var show = Show(Offer(Northstar, VideoQuality.Uhd, OfferKind.Rent), Offer(Lanternbox, VideoQuality.Hd, OfferKind.Addon));

        Assert.False(TitleHomes.Choose(show, Ours, HomeOverrides.None).OnOurServices);
    }

    [Fact]
    public void AFilmPlaysTheVideoLinkOfItsHome()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd, link: "https://watch.example.com/n/larkspur", video: "https://watch.example.com/n/larkspur/play"));

        var link = TitleHomes.LinkFor(film, Northstar, null);

        Assert.NotNull(link);
        Assert.Equal(Larkspur, link.Title);
        Assert.Equal(Northstar, link.Service);
        Assert.Equal(new Uri("https://watch.example.com/n/larkspur"), link.Link);
        Assert.Equal(new Uri("https://watch.example.com/n/larkspur/play"), link.VideoLink);
    }

    [Fact]
    public void AnEpisodeWithItsOwnLinkOnTheHomePlaysThatLink()
    {
        var show = Show(Offer(Northstar, VideoQuality.Uhd, link: "https://watch.example.com/n/saltmarsh"));
        var season = show.Seasons[0];
        var third = season.Episodes[2] with
        {
            Offers = [Offer(Northstar, VideoQuality.Uhd, link: "https://watch.example.com/n/saltmarsh/s1e3")],
        };
        show = show with { Seasons = [season with { Episodes = [.. season.Episodes.Take(2), third, .. season.Episodes.Skip(3)] }, show.Seasons[1]] };

        Assert.Equal(new Uri("https://watch.example.com/n/saltmarsh/s1e3"), TitleHomes.LinkFor(show, Northstar, E(1, 3))!.Link);
        Assert.Equal(new Uri("https://watch.example.com/n/saltmarsh"), TitleHomes.LinkFor(show, Northstar, E(1, 4))!.Link);
    }

    [Fact]
    public void NoHttpsLinkOnTheHomeMeansNothingToHandOff()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd, link: "http://watch.example.com/n/larkspur"));

        Assert.Null(TitleHomes.LinkFor(film, Northstar, null));
        Assert.Null(TitleHomes.LinkFor(Film(Offer(Lanternbox, VideoQuality.Hd)), Northstar, null));
    }

    [Fact]
    public void AnInsecureVideoLinkIsDroppedButTheTitleLinkStands()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd, link: "https://watch.example.com/n/larkspur", video: "http://watch.example.com/n/play"));

        var link = TitleHomes.LinkFor(film, Northstar, null)!;

        Assert.Null(link.VideoLink);
        Assert.Equal(new Uri("https://watch.example.com/n/larkspur"), link.Link);
    }

    [Fact]
    public void TheSessionNamesAreTheTitleAndTheEpisode()
    {
        Assert.Equal(["Saltmarsh", "Episode 1-3"], TitleHomes.SessionNames(Show(), E(1, 3)));
        Assert.Equal(["Larkspur"], TitleHomes.SessionNames(Film(), null));
    }

    [Fact]
    public void WhyThisRemembersOnlyThisViewersFinishedTitlesThatShareAGenre()
    {
        var me = new ViewerId("ash-example");
        var lantern = new TitleId("brass-lantern-example");
        var harbour = new TitleId("harbour-comedy-example");
        var titles = new Dictionary<TitleId, CatalogueTitle>
        {
            [lantern] = Film() with { Id = lantern, Name = "The Brass Lantern", Genres = ["Mystery"] },
            [harbour] = Film() with { Id = harbour, Name = "Harbour Lights", Genres = ["Comedy"] },
        };
        var sittings = new[]
        {
            new WatchEvent(lantern, WatchedKind.Film, Now.AddDays(-3), Northstar, Ended: true),
            new WatchEvent(harbour, WatchedKind.Film, Now.AddDays(-1), Northstar, Ended: true),
            Watched(E(1, 1), Now.AddDays(-2)),
        };

        var past = WhyThisPast.Alike(me, Show(), sittings, id => titles.GetValueOrDefault(id));

        var only = Assert.Single(past);
        Assert.Equal("The Brass Lantern", only.Name);
        Assert.Equal(me, only.Viewer);
        Assert.Equal(Now.AddDays(-3), only.FinishedAt);
    }

    [Fact]
    public void WhyThisSkipsAFilmStoppedShortAndATitleNotInTheCache()
    {
        var me = new ViewerId("ash-example");
        var stopped = new TitleId("stopped-film-example");
        var unknown = new TitleId("unknown-film-example");
        var titles = new Dictionary<TitleId, CatalogueTitle>
        {
            [stopped] = Film() with { Id = stopped, Name = "Half Tide", Genres = ["Mystery"] },
        };
        var sittings = new[]
        {
            new WatchEvent(stopped, WatchedKind.Film, Now.AddDays(-3), Northstar, StoppedAt: TimeSpan.FromMinutes(10), Runtime: TimeSpan.FromMinutes(100)),
            new WatchEvent(unknown, WatchedKind.Film, Now.AddDays(-3), Northstar, Ended: true),
        };

        Assert.Empty(WhyThisPast.Alike(me, Show(), sittings, id => titles.GetValueOrDefault(id)));
    }

    [Fact]
    public void WhyThisCountsAShowFinishedOnlyWhenItsLastEpisodeIsWatched()
    {
        var me = new ViewerId("ash-example");
        var ferry = new TitleId("ferry-show-example");
        var ferryShow = Show() with { Id = ferry, Name = "The Ferry", Seasons = [Season(1, 2, Now.AddDays(-100))] };
        var halfway = new[] { new WatchEvent(ferry, WatchedKind.Show, Now.AddDays(-5), Northstar, E(1, 1), Ended: true) };
        var finished = halfway.Append(new WatchEvent(ferry, WatchedKind.Show, Now.AddDays(-4), Northstar, E(1, 2), Ended: true)).ToList();

        Assert.Empty(WhyThisPast.Alike(me, Film() with { Genres = ["Mystery"] }, halfway, _ => ferryShow));
        Assert.Equal(Now.AddDays(-4), Assert.Single(WhyThisPast.Alike(me, Film() with { Genres = ["Mystery"] }, finished, _ => ferryShow)).FinishedAt);
    }
}
