using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Tests.Library;

/// <summary>
/// The words on a playlist row (mockup 04, D225): the kind and length; where it plays, "free, with
/// ads" or "4K" as the offer says; a date it leaves on the row only (D124, D146); "not on your
/// services" with the one rent/buy line (D20, D53); "· watched" on a struck row (D101); "Caught up ·
/// Season N out <date>" on a dimmed show (D224) and the date on a film not yet out (D32). Invented
/// services, titles and prices.
/// </summary>
public class PlaylistPageRowTextTests
{
    private static readonly DateOnly Today = new(2026, 3, 12);

    private static readonly ServiceId Streamly = new("streamly-example");
    private static readonly ServiceId Kestrel = new("kestrel-example");
    private static readonly ServiceId Northstar = new("northstar-example");
    private static readonly ServiceId Reelhouse = new("reelhouse-example");
    private static readonly IReadOnlyList<ServiceId> Ours = [Streamly, Kestrel, Northstar];

    private static TitleOffer Offer(ServiceId service, string name, OfferKind kind, VideoQuality quality = VideoQuality.Hd, decimal? price = null, DateOnly? expires = null, bool soon = false) =>
        new(service, name, KnownService: false, kind, quality, price, price is null ? null : "USD", "https://example.com/watch", null, expires, soon);

    private static CatalogueTitle Film(string name, int? runtime, params TitleOffer[] offers) =>
        new() { Id = new TitleId(name.ToLowerInvariant().Replace(' ', '-')), Kind = TitleKind.Film, Name = name, RuntimeMinutes = runtime, Offers = offers };

    private static CatalogueTitle Show(string name, int? seasons, params TitleOffer[] offers) =>
        new() { Id = new TitleId(name.ToLowerInvariant().Replace(' ', '-')), Kind = TitleKind.Show, Name = name, SeasonCount = seasons, Offers = offers };

    private static PlaylistRow Row(CatalogueTitle title, bool struck = false, RowDim dim = RowDim.None, int? nextSeason = null, DateOnly? until = null) =>
        new(PlaylistEntry.ForTitle(title.Id, title.Kind), struck, dim, nextSeason, until);

    private static PlaylistRowLines Lines(PlaylistRow row, CatalogueTitle? title) =>
        PlaylistPageRowText.For(row, title, Ours, HomeOverrides.None, Today);

    [Fact]
    public void AFreeShowSaysItHasAds()
    {
        var crow = Show("The Cozy Crow", 3, Offer(Kestrel, "Kestrel TV", OfferKind.Free));

        var lines = Lines(Row(crow), crow);

        Assert.Equal("The Cozy Crow", lines.Name);
        Assert.Equal("Show · 3 seasons", lines.KindLine);
        Assert.Equal("On Kestrel TV · free, with ads", lines.WhereLine);
        Assert.False(lines.WhereEmphasis);
    }

    [Fact]
    public void AFilmInFourKSaysSo()
    {
        var majestic = Film("The Majestic", 104, Offer(Streamly, "Streamly", OfferKind.Subscription, VideoQuality.Uhd));

        var lines = Lines(Row(majestic), majestic);

        Assert.Equal("Film · 1h 44m", lines.KindLine);
        Assert.Equal("On Streamly · 4K", lines.WhereLine);
    }

    [Fact]
    public void AWatchedRowIsStruckWithNoPlace()
    {
        var tide = Film("Tidewater", 91, Offer(Streamly, "Streamly", OfferKind.Subscription));

        var lines = Lines(Row(tide, struck: true), tide);

        Assert.Equal("Film · 1h 31m · watched", lines.KindLine);
        Assert.Null(lines.WhereLine);
    }

    [Fact]
    public void OffOurServicesGivesTheOneRentLine()
    {
        var gate = Film(
            "The Iron Gate",
            126,
            Offer(Reelhouse, "Reelhouse", OfferKind.Rent, VideoQuality.Hd, 3.99m),
            Offer(Reelhouse, "Reelhouse", OfferKind.Buy, VideoQuality.Hd, 14.99m));

        var lines = Lines(Row(gate), gate);

        Assert.Equal("Film · 2h 06m", lines.KindLine);
        Assert.Equal("Not on your services · rent from Reelhouse, $3.99 HD", lines.WhereLine);
        Assert.True(lines.WhereEmphasis);
    }

    [Fact]
    public void OffOurServicesWithNothingToRentSaysOnlyThat()
    {
        var gate = Film("The Iron Gate", 126);

        Assert.Equal("Not on your services", Lines(Row(gate), gate).WhereLine);
    }

    [Fact]
    public void ACaughtUpShowSaysWhenItsNextSeasonIsOut()
    {
        var orchard = Show("The Orchard Line", 2, Offer(Streamly, "Streamly", OfferKind.Subscription));

        var lines = Lines(Row(orchard, dim: RowDim.CaughtUp, nextSeason: 2, until: new DateOnly(2026, 3, 14)), orchard);

        Assert.Equal("Caught up · Season 2 out Mar 14", lines.KindLine);
        Assert.Null(lines.WhereLine);
    }

    [Fact]
    public void ACaughtUpShowWithNothingAnnouncedSaysOnlyCaughtUp()
    {
        var orchard = Show("The Orchard Line", 2);

        Assert.Equal("Caught up", Lines(Row(orchard, dim: RowDim.CaughtUp), orchard).KindLine);
    }

    [Fact]
    public void AFilmNotOutYetCarriesItsDate()
    {
        var pass = Film("High Pass", 98);

        var lines = Lines(Row(pass, dim: RowDim.NotOutYet, until: new DateOnly(2026, 11, 12)), pass);

        Assert.Equal("Film · 1h 38m · out Nov 12", lines.KindLine);
        Assert.Null(lines.WhereLine);
    }

    [Fact]
    public void LeavingSoonPutsTheDateOnTheRowInsteadOfTheService()
    {
        var pass = Film("High Pass", 98, Offer(Northstar, "Northstar+", OfferKind.Subscription, expires: new DateOnly(2026, 10, 31), soon: true));

        Assert.Equal("Leaves Northstar+ Oct 31", Lines(Row(pass), pass).WhereLine);
    }

    [Fact]
    public void ShortLengthsAndSingleSeasonsReadNaturally()
    {
        var short1 = Film("Paper Boats", 52);
        var one = Show("Saltmarsh", 1);

        Assert.Equal("Film · 52m", Lines(Row(short1), short1).KindLine);
        Assert.Equal("Show · 1 season", Lines(Row(one), one).KindLine);
    }

    [Fact]
    public void ATitleNotYetLookedUpShowsItsKindAndNoPlace()
    {
        var entry = PlaylistEntry.ForTitle(new TitleId("unlooked-example"), TitleKind.Film);

        var lines = PlaylistPageRowText.For(new PlaylistRow(entry, false, RowDim.None, null, null), null, Ours, HomeOverrides.None, Today);

        Assert.Equal("Film", lines.KindLine);
        Assert.Null(lines.WhereLine);
        Assert.Null(lines.Name);
    }
}
