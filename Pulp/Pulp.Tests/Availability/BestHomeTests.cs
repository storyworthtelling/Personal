using Pulp.Domain.Availability;
using Pulp.Domain.Core;

namespace Pulp.Tests.Availability;

/// <summary>
/// D28: of the household's services that carry a title, open on the best quality; D29: the plan's
/// ad tier breaks a quality tie; D216: "open there instead" pins a title to a home from then on.
/// Every service here is invented.
/// </summary>
public class BestHomeTests
{
    private static readonly TitleId Marsh = new("saltmarsh-example");
    private static readonly ServiceId Northstar = new("northstar-example");
    private static readonly ServiceId Lanternbox = new("lanternbox-example");
    private static readonly ServiceId Kestrel = new("kestrel-example");
    private static readonly ServiceId Elsewhere = new("elsewhere-example");

    private static readonly ServicePlan[] Household =
    [
        new(Northstar, AdFree: true),
        new(Lanternbox, AdFree: false),
        new(Kestrel, AdFree: true),
    ];

    [Fact]
    public void HigherQualityWinsEvenWithAds()
    {
        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Northstar, VideoQuality.Hd), new StreamOffer(Lanternbox, VideoQuality.Uhd)],
            Household,
            HomeOverrides.None);

        Assert.Equal(Lanternbox, choice.Best!.Service);
        Assert.True(choice.Best.WithAds);
    }

    [Fact]
    public void AtEqualQualityTheAdFreeHomeWins()
    {
        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Lanternbox, VideoQuality.Uhd), new StreamOffer(Northstar, VideoQuality.Uhd)],
            Household,
            HomeOverrides.None);

        Assert.Equal(Northstar, choice.Best!.Service);
        Assert.False(choice.Best.WithAds);
        Assert.Equal([Lanternbox], choice.Others.Select(h => h.Service));
    }

    [Fact]
    public void AFreeAdSupportedOfferCarriesAdsWhateverThePlan()
    {
        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Kestrel, VideoQuality.Hd, AlwaysWithAds: true), new StreamOffer(Northstar, VideoQuality.Hd)],
            Household,
            HomeOverrides.None);

        Assert.Equal(Northstar, choice.Best!.Service);
        Assert.True(choice.Others.Single().WithAds);
    }

    [Fact]
    public void AServiceTheHouseholdDoesNotHaveIsNeverAHome()
    {
        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Elsewhere, VideoQuality.Uhd)],
            Household,
            HomeOverrides.None);

        Assert.Null(choice.Best);
        Assert.Empty(choice.Others);
        Assert.False(choice.OnOurServices);
    }

    [Fact]
    public void TheSameServiceOfferedTwiceIsOneHomeAtItsBestQuality()
    {
        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Northstar, VideoQuality.Sd), new StreamOffer(Northstar, VideoQuality.Uhd)],
            Household,
            HomeOverrides.None);

        Assert.Equal(VideoQuality.Uhd, choice.Best!.Quality);
        Assert.Empty(choice.Others);
    }

    [Fact]
    public void AnOverrideMakesTheChosenServiceThisTitlesHome()
    {
        var overrides = HomeOverrides.None.OpenThereInstead(Marsh, Lanternbox);

        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Northstar, VideoQuality.Uhd), new StreamOffer(Lanternbox, VideoQuality.Hd)],
            Household,
            overrides);

        Assert.Equal(Lanternbox, choice.Best!.Service);
        Assert.Equal([Northstar], choice.Others.Select(h => h.Service));
    }

    [Fact]
    public void AnOverrideTouchesOnlyItsOwnTitle()
    {
        var overrides = HomeOverrides.None.OpenThereInstead(new TitleId("another-title"), Lanternbox);

        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Northstar, VideoQuality.Uhd), new StreamOffer(Lanternbox, VideoQuality.Hd)],
            Household,
            overrides);

        Assert.Equal(Northstar, choice.Best!.Service);
    }

    [Fact]
    public void AnOverrideWhoseServiceNoLongerCarriesTheTitleFallsBackToTheRule()
    {
        var overrides = HomeOverrides.None.OpenThereInstead(Marsh, Lanternbox);

        var choice = BestHome.Choose(
            Marsh,
            [new StreamOffer(Northstar, VideoQuality.Hd)],
            Household,
            overrides);

        Assert.Equal(Northstar, choice.Best!.Service);
    }

    [Fact]
    public void OverridesAreImmutableAndTheLatestChoiceStands()
    {
        var first = HomeOverrides.None.OpenThereInstead(Marsh, Lanternbox);
        var second = first.OpenThereInstead(Marsh, Kestrel);

        Assert.Equal(Lanternbox, first.For(Marsh));
        Assert.Equal(Kestrel, second.For(Marsh));
        Assert.Null(HomeOverrides.None.For(Marsh));
    }

    [Fact]
    public void TheChoiceDoesNotDependOnTheOrderOffersArrive()
    {
        StreamOffer[] offers =
        [
            new(Kestrel, VideoQuality.Hd),
            new(Northstar, VideoQuality.Hd),
        ];

        var forward = BestHome.Choose(Marsh, offers, Household, HomeOverrides.None);
        var backward = BestHome.Choose(Marsh, offers.Reverse(), Household, HomeOverrides.None);

        Assert.Equal(forward.Best!.Service, backward.Best!.Service);
    }
}
