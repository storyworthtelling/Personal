using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Title;
using Pulp.Domain.Watching;
using static Pulp.Tests.Title.TitleFixtures;

namespace Pulp.Tests.Availability;

/// <summary>
/// The plan Settings records (D29, D86) reaches every screen that says where a title plays: the title
/// page (<see cref="TitleHomes"/>), a Continue watching row (<see cref="ContinueRowText"/>) and a playlist
/// row (<see cref="PlaylistPageRowText"/>). Before this, each built its plans from the bare service ids,
/// every one ad-free, so a with-ads subscription was shown as ad-free and could win a tie it should lose.
///
/// <para>The picture recorded on the plan caps what <see cref="BestHome"/> may claim for that service:
/// D28 opens on the best picture <i>the household would see</i>, and an HD plan streams HD whatever the
/// catalogue lists the title at. Invented services and titles only.</para>
/// </summary>
public class PlanTierEverywhereTests
{
    private static readonly ServicePlan NorthstarWithAds = new(Northstar, AdFree: false);
    private static readonly ServicePlan LanternboxAdFree = new(Lanternbox, AdFree: true);

    private static string NameOf(ServiceId service) => service == Northstar ? "Northstar" : "Lanternbox";

    [Fact]
    public void TheTitlePageReadsAWithAdsPlan()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd));

        var choice = TitleHomes.Choose(film, [NorthstarWithAds], HomeOverrides.None);

        Assert.True(choice.Best!.WithAds);
        Assert.Equal("On Northstar · HD, with ads", TitlePageText.WhereLine(choice.Best, "Northstar"));
    }

    [Fact]
    public void AContinueRowSaysAWithAdsSubscriptionHasAds()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd));

        Assert.Equal("On Northstar · HD, with ads", ContinueRowText.Where(film, [NorthstarWithAds], HomeOverrides.None, NameOf));
        Assert.True(ContinueRowText.Home(film, [NorthstarWithAds], HomeOverrides.None)!.WithAds);
    }

    [Fact]
    public void AContinueRowStillSaysFreeForAFreeService()
    {
        var film = Film(Offer(Lanternbox, VideoQuality.Hd, OfferKind.Free));

        Assert.Equal("On Lanternbox · free, with ads", ContinueRowText.Where(film, [LanternboxAdFree], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void AnAdFreeContinueRowIsUnchanged()
    {
        var film = Film(Offer(Lanternbox, VideoQuality.Uhd));

        Assert.Equal("On Lanternbox · 4K", ContinueRowText.Where(film, [LanternboxAdFree], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void APlaylistRowSaysAWithAdsSubscriptionHasAds()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd));
        var row = new PlaylistRow(PlaylistEntry.ForTitle(film.Id, film.Kind), false, RowDim.None, null, null);

        var lines = PlaylistPageRowText.For(row, film, [NorthstarWithAds], HomeOverrides.None, new DateOnly(2026, 3, 12));

        Assert.Equal("On " + Northstar.Value + " · HD, with ads", lines.WhereLine);
    }

    [Fact]
    public void AnAdFreePlaylistRowIsUnchanged()
    {
        var film = Film(Offer(Lanternbox, VideoQuality.Uhd));
        var row = new PlaylistRow(PlaylistEntry.ForTitle(film.Id, film.Kind), false, RowDim.None, null, null);

        var lines = PlaylistPageRowText.For(row, film, [LanternboxAdFree], HomeOverrides.None, new DateOnly(2026, 3, 12));

        Assert.Equal("On " + Lanternbox.Value + " · 4K", lines.WhereLine);
    }

    /// <summary>A 4K listing on an HD plan streams HD: the home is HD, and the page says HD, not 4K.</summary>
    [Fact]
    public void ThePlansPictureCapsTheHomesPicture()
    {
        var film = Film(Offer(Northstar, VideoQuality.Uhd));

        var choice = TitleHomes.Choose(film, [new ServicePlan(Northstar, AdFree: true, Picture: VideoQuality.Hd)], HomeOverrides.None);

        Assert.Equal(VideoQuality.Hd, choice.Best!.Quality);
    }

    /// <summary>
    /// Capped, the 4K listing ties the other service's HD; the tie then falls to the service id, so
    /// Lanternbox opens. Uncapped, Northstar's claimed 4K would win a picture it cannot deliver.
    /// </summary>
    [Fact]
    public void ACappedPictureNoLongerWinsOnAPictureItCannotDeliver()
    {
        var film = Film(Offer(Northstar, VideoQuality.Uhd), Offer(Lanternbox, VideoQuality.Hd));
        ServicePlan[] plans = [new(Northstar, AdFree: true, Picture: VideoQuality.Hd), LanternboxAdFree];

        var choice = TitleHomes.Choose(film, plans, HomeOverrides.None);

        Assert.Equal(Lanternbox, choice.Best!.Service);
    }

    [Fact]
    public void APlanPictureAboveTheListingDoesNotRaiseIt()
    {
        var film = Film(Offer(Northstar, VideoQuality.Hd));

        var choice = TitleHomes.Choose(film, [new ServicePlan(Northstar, AdFree: true, Picture: VideoQuality.Uhd)], HomeOverrides.None);

        Assert.Equal(VideoQuality.Hd, choice.Best!.Quality);
    }

    [Fact]
    public void TheHouseholdsPlansCarryTheRecordedPicture()
    {
        var stamp = new HlcTimestamp(10, 0);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Northstar), stamp, "install-example");
        document = HouseholdRecords.Put(document, HouseholdMaps.ServicePlan,
            new ServicePlanEntry(Northstar) { Tier = PlanTier.WithAds, Picture = VideoQuality.Hd }, new HlcTimestamp(20, 0), "install-example");

        var plan = Assert.Single(HouseholdView.Of(document).ServicePlans);

        Assert.Equal(new ServicePlan(Northstar, AdFree: false, Picture: VideoQuality.Hd), plan);
    }
}
