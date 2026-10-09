using Pulp.Domain.Availability;
using Pulp.Domain.Core;

namespace Pulp.Tests.Availability;

/// <summary>D53: one line, the cheapest rent or buy at HD or better — never a price list. Invented stores and prices.</summary>
public class RentLineTests
{
    private static readonly ServiceId Reelhouse = new("reelhouse-example");
    private static readonly ServiceId Cinestall = new("cinestall-example");

    [Fact]
    public void TheCheapestOfferAtHdOrBetterIsTheLine()
    {
        var line = RentLine.Cheapest(
        [
            new PaidOffer(Reelhouse, PaidKind.Rent, VideoQuality.Hd, 3.99m),
            new PaidOffer(Cinestall, PaidKind.Rent, VideoQuality.Uhd, 5.99m),
            new PaidOffer(Cinestall, PaidKind.Buy, VideoQuality.Hd, 12.99m),
        ]);

        Assert.Equal(Reelhouse, line!.Store);
        Assert.Equal(3.99m, line.Price);
    }

    [Fact]
    public void ACheaperStandardDefinitionOfferIsNeverTheLine()
    {
        var line = RentLine.Cheapest(
        [
            new PaidOffer(Cinestall, PaidKind.Rent, VideoQuality.Sd, 0.99m),
            new PaidOffer(Reelhouse, PaidKind.Rent, VideoQuality.Hd, 3.99m),
        ]);

        Assert.Equal(Reelhouse, line!.Store);
    }

    [Fact]
    public void OnlyStandardDefinitionOnOfferMeansNoLine()
    {
        Assert.Null(RentLine.Cheapest([new PaidOffer(Cinestall, PaidKind.Buy, VideoQuality.Sd, 4.99m)]));
        Assert.Null(RentLine.Cheapest([]));
    }

    [Fact]
    public void AtTheSamePriceTheBetterPictureWinsThenRentBeforeBuy()
    {
        var byQuality = RentLine.Cheapest(
        [
            new PaidOffer(Reelhouse, PaidKind.Rent, VideoQuality.Hd, 3.99m),
            new PaidOffer(Cinestall, PaidKind.Rent, VideoQuality.Uhd, 3.99m),
        ]);
        var byKind = RentLine.Cheapest(
        [
            new PaidOffer(Reelhouse, PaidKind.Buy, VideoQuality.Hd, 3.99m),
            new PaidOffer(Cinestall, PaidKind.Rent, VideoQuality.Hd, 3.99m),
        ]);

        Assert.Equal(Cinestall, byQuality!.Store);
        Assert.Equal(PaidKind.Rent, byKind!.Kind);
    }

    [Fact]
    public void ANegativePriceIsRefusedRatherThanBecomingTheCheapest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaidOffer(Reelhouse, PaidKind.Rent, VideoQuality.Hd, -1m));
    }
}
