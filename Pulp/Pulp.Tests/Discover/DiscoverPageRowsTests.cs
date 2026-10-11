using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using static Pulp.Tests.Discover.DiscoverFixtures;

namespace Pulp.Tests.Discover;

/// <summary>
/// The row anatomy mockup 03 draws beside the front poster, which search and mood results reuse
/// (D272): the title, then "Film · 1h 44m" or "Show · 2 seasons", then where it plays — "On Streamly ·
/// 4K", or "· with ads" where that applies (D29). A title off the household's services says where it
/// streams instead (D107, D20); one that streams nowhere says so, and its title page carries the rent
/// line (D53). Invented services and titles only.
/// </summary>
public class DiscoverPageRowsTests
{
    private static readonly IReadOnlyList<ServicePlan> AdFreePlans =
        [new ServicePlan(Flix.Id, AdFree: true), new ServicePlan(Sample.Id, AdFree: true)];

    private static CatalogueTitle WithOffers(CatalogueTitle title, params TitleOffer[] offers) => title with { Offers = offers };

    private static TitleOffer Offer(Pulp.Domain.Services.StreamingService service, OfferKind kind, VideoQuality quality, decimal? price = null) =>
        new(service.Id, service.Name, true, kind, quality, price, price is null ? null : "USD", "https://example.com/w", null, null, false);

    [Fact]
    public void AFilmReadsItsRuntimeInHoursAndMinutes()
    {
        var row = DiscoverPageRows.For(Glasshouse with { RuntimeMinutes = 104 }, AdFreePlans, HomeOverrides.None);

        Assert.Equal("The Glasshouse Year", row.Name);
        Assert.Equal("Film · 1h 44m", row.KindLine);
    }

    [Theory]
    [InlineData(45, "Film · 45m")]
    [InlineData(120, "Film · 2h")]
    [InlineData(null, "Film")]
    public void AFilmsRuntimeDropsWhatItDoesNotHave(int? minutes, string expected) =>
        Assert.Equal(expected, DiscoverPageRows.For(Glasshouse with { RuntimeMinutes = minutes }, AdFreePlans, HomeOverrides.None).KindLine);

    [Theory]
    [InlineData(2, "Show · 2 seasons")]
    [InlineData(1, "Show · 1 season")]
    [InlineData(null, "Show")]
    public void AShowReadsItsSeasons(int? seasons, string expected) =>
        Assert.Equal(expected, DiscoverPageRows.For(Kettle with { SeasonCount = seasons }, AdFreePlans, HomeOverrides.None).KindLine);

    [Fact]
    public void OnOurServicesTheBestHomeAndItsPictureAreNamed()
    {
        var title = WithOffers(Glasshouse, Offer(Flix, OfferKind.Subscription, VideoQuality.Uhd), Offer(Sample, OfferKind.Subscription, VideoQuality.Hd));
        var row = DiscoverPageRows.For(title, AdFreePlans, HomeOverrides.None);

        Assert.True(row.OnYourServices);
        Assert.Equal("On Exampleflix · 4K", row.HomeLine);
    }

    [Fact]
    public void AnAdTierPlanSaysWithAdsInsteadOfThePicture()
    {
        var title = WithOffers(Kettle, Offer(Sample, OfferKind.Subscription, VideoQuality.Hd));
        var row = DiscoverPageRows.For(title, [new ServicePlan(Sample.Id, AdFree: false)], HomeOverrides.None);

        Assert.Equal("On Samplestream · with ads", row.HomeLine);
    }

    [Fact]
    public void OffOurServicesTheRowSaysWhereItStreamsInstead()
    {
        var row = DiscoverPageRows.For(OffServices, AdFreePlans, HomeOverrides.None);

        Assert.False(row.OnYourServices);
        Assert.Equal("On Elsewhere TV · not yours", row.HomeLine);
    }

    [Fact]
    public void ATitleThatStreamsNowhereSaysSoAndPointsAtTheRentLine()
    {
        var rentOnly = WithOffers(Nightwater, Offer(Elsewhere, OfferKind.Rent, VideoQuality.Hd, 3.99m));
        var nothing = WithOffers(Nightwater);

        Assert.Equal("Not streaming · rent or buy", DiscoverPageRows.For(rentOnly, AdFreePlans, HomeOverrides.None).HomeLine);
        Assert.Equal("Not streaming", DiscoverPageRows.For(nothing, AdFreePlans, HomeOverrides.None).HomeLine);
    }

    [Fact]
    public void TheRowCarriesTheGenresAsTheTagsAVetoRemembers()
    {
        var row = DiscoverPageRows.For(Glasshouse with { Genres = ["Drama", "Mystery"], PosterUrl = "https://example.com/p.jpg" }, AdFreePlans, HomeOverrides.None);

        Assert.Equal(["Drama", "Mystery"], row.Tags);
        Assert.Equal("https://example.com/p.jpg", row.PosterUrl);
        Assert.Equal(Glasshouse.Id, row.Id);
        Assert.Equal(TitleKind.Film, row.Kind);
        Assert.Equal(2019, row.Year);
    }
}
