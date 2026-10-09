using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using static Pulp.Tests.Catalogue.CatalogueFixtures;

namespace Pulp.Tests.Catalogue;

public class TitleOffersTests
{
    [Fact]
    public void SubscriptionAndFreeOffersBecomeStreamsAndFreeAlwaysCarriesAds()
    {
        var streams = TitleOffers.Streams(
        [
            Offer("northstar", OfferKind.Subscription, VideoQuality.Uhd),
            Offer("driftwood", OfferKind.Free, VideoQuality.Sd),
        ]);

        Assert.Equal(
            [
                new StreamOffer(new ServiceId("northstar"), VideoQuality.Uhd, AlwaysWithAds: false),
                new StreamOffer(new ServiceId("driftwood"), VideoQuality.Sd, AlwaysWithAds: true),
            ],
            streams);
    }

    [Fact]
    public void AnAddonIsNotAStreamOnTheServiceItRidesOn()
    {
        // The bug this stops: a channel bought inside a service opening the title on that service.
        var streams = TitleOffers.Streams([Offer("northstar", OfferKind.Addon), Offer("northstar", OfferKind.Rent, price: 3.99m, currency: "USD")]);

        Assert.Empty(streams);
    }

    [Fact]
    public void RentAndBuyWithAPriceBecomePaidOffersForTheRentLine()
    {
        var paid = TitleOffers.Paid(
        [
            Offer("corner-store", OfferKind.Rent, VideoQuality.Hd, 3.99m, "USD", known: false),
            Offer("corner-store", OfferKind.Buy, VideoQuality.Uhd, 14.99m, "USD", known: false),
            Offer("northstar", OfferKind.Subscription),
        ]);

        Assert.Equal(2, paid.Count);
        Assert.Equal(new PaidOffer(new ServiceId("corner-store"), PaidKind.Rent, VideoQuality.Hd, 3.99m), paid[0]);
        Assert.Equal(new PaidOffer(new ServiceId("corner-store"), PaidKind.Buy, VideoQuality.Uhd, 14.99m), paid[1]);
        Assert.Equal(paid[0], RentLine.Cheapest(paid));
    }

    [Fact]
    public void APaidOfferWithNoPriceOrAnotherCurrencyIsLeftOut()
    {
        // A season sold only by the episode has no price, and a price in another currency is not comparable.
        var paid = TitleOffers.Paid(
        [
            Offer("corner-store", OfferKind.Buy, price: null),
            Offer("far-shop", OfferKind.Rent, price: 1.00m, currency: "EUR"),
        ]);

        Assert.Empty(paid);
    }
}
