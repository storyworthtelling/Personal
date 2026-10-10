using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Watching;

namespace Pulp.Tests.Watching;

/// <summary>
/// A Continue watching row's words (mockup 01, D150, D28, D29, D121): what's next, where it plays and
/// how, and the pencil mark's count. Every title, service and viewer here is invented.
/// </summary>
public class ContinueRowTextTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 12, 21, 0, 0, TimeSpan.Zero);
    private static readonly TitleId Marsh = new("marsh-show-example");
    private static readonly TitleId Prairie = new("prairie-film-example");
    private static readonly ServiceId Northlight = new("northlight-example");
    private static readonly ServiceId Lampbox = new("lampbox-example");
    private static readonly ServiceId Kite = new("kite-example");

    private static string NameOf(ServiceId service) =>
        service == Northlight ? "Northlight+" : service == Lampbox ? "Lampbox" : service == Kite ? "Kite TV" : service.Value;

    private static TitleOffer Offer(ServiceId service, OfferKind kind, VideoQuality quality) =>
        new(service, NameOf(service), true, kind, quality, null, null, "https://example.com/watch/" + service.Value, null, null, false);

    private static CatalogueTitle Show(params TitleOffer[] offers) => new()
    {
        Id = Marsh,
        Kind = TitleKind.Show,
        Name = "The Marsh Lamps",
        Offers = offers,
        Seasons =
        [
            new CatalogueSeason(1, "Season 1",
            [
                new CatalogueEpisode(new EpisodeRef(1, 2), "High Water", string.Empty, null, null, null, []),
                new CatalogueEpisode(new EpisodeRef(1, 3), "Slack Tide", string.Empty, null, null, null, []),
                new CatalogueEpisode(new EpisodeRef(1, 4), "  ", string.Empty, null, null, null, []),
            ]),
        ],
    };

    private static ContinueRow ShowRow(EpisodeRef? next, ProgressMark? mark) =>
        new(Marsh, TitleKind.Show, At, NewEpisode: false, next, mark);

    private static ContinueRow FilmRow(ProgressMark? mark) =>
        new(Prairie, TitleKind.Film, At, NewEpisode: false, NextUp: null, mark);

    // ---- the pencil mark's count (D150) ----

    [Fact]
    public void AnEpisodeMarkCountsTheEpisodeOfTheSeason() =>
        Assert.Equal("E3 of 6", ContinueRowText.Count(new ProgressMark(3, 6, ProgressUnit.Episode)));

    [Fact]
    public void AMinuteMarkCountsMinutes() =>
        Assert.Equal("46 of 118 min", ContinueRowText.Count(new ProgressMark(46, 118, ProgressUnit.Minute)));

    [Theory]
    [InlineData(3, 6, 0.5)]
    [InlineData(46, 118, 46d / 118d)]
    [InlineData(1, 10, 0.1)]
    [InlineData(9, 4, 1.0)]
    [InlineData(2, 0, 0.0)]
    public void TheMarkIsDrawnAsTheShareOfTheCount(int at, int of, double expected) =>
        Assert.Equal(expected, ContinueRowText.Fraction(new ProgressMark(at, of, ProgressUnit.Episode)), 6);

    // ---- what's next ----

    [Fact]
    public void AShowsNextUpNamesTheEpisodeAsTheCatalogueDoes() =>
        Assert.Equal("S1 · E3 · Slack Tide", ContinueRowText.NextUp(ShowRow(new EpisodeRef(1, 3), null), Show()));

    [Fact]
    public void AnEpisodeWithNoNameIsJustItsNumber() =>
        Assert.Equal("S1 · E4", ContinueRowText.NextUp(ShowRow(new EpisodeRef(1, 4), null), Show()));

    [Fact]
    public void WithNothingCachedTheEpisodeIsStillNumbered() =>
        Assert.Equal("S2 · E5", ContinueRowText.NextUp(ShowRow(new EpisodeRef(2, 5), null), null));

    [Fact]
    public void AShowWhoseScheduleIsUnknownSaysNothingAboutNext() =>
        Assert.Null(ContinueRowText.NextUp(ShowRow(null, null), Show()));

    [Fact]
    public void AFilmSaysHowLongIsLeft() =>
        Assert.Equal("Film · 1h 12m left", ContinueRowText.NextUp(FilmRow(new ProgressMark(46, 118, ProgressUnit.Minute)), null));

    [Fact]
    public void AFilmWithNoMarkIsJustAFilm() =>
        Assert.Equal("Film", ContinueRowText.NextUp(FilmRow(null), null));

    [Theory]
    [InlineData(72, "1h 12m left")]
    [InlineData(45, "45m left")]
    [InlineData(120, "2h left")]
    [InlineData(61, "1h 1m left")]
    [InlineData(0, "Under a minute left")]
    public void TimeLeftReadsInHoursAndMinutes(int minutes, string expected) =>
        Assert.Equal(expected, ContinueRowText.Left(minutes));

    [Fact]
    public void NegativeTimeLeftIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ContinueRowText.Left(-1));

    [Fact]
    public void TheCardNamesTheEpisodeAndTheMarkNamesItShort()
    {
        Assert.Equal("S1 · E3", ContinueRowText.Episode(new EpisodeRef(1, 3)));
        Assert.Equal("E3", ContinueRowText.EpisodeShort(new EpisodeRef(1, 3)));
    }

    // ---- where it plays and how (D28, D29, D121) ----

    [Fact]
    public void TheBestQualityHomeIsNamedWithItsPicture()
    {
        // D28: the marsh is also on Lampbox with ads, so it says Northlight+.
        var title = Show(Offer(Lampbox, OfferKind.Free, VideoQuality.Hd), Offer(Northlight, OfferKind.Subscription, VideoQuality.Uhd));
        Assert.Equal("On Northlight+ · 4K", ContinueRowText.Where(title, [Northlight, Lampbox], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void AFreeServiceSaysFreeWithAdsInsteadOfThePicture()
    {
        var title = Show(Offer(Kite, OfferKind.Free, VideoQuality.Hd));
        Assert.Equal("On Kite TV · free, with ads", ContinueRowText.Where(title, [Kite], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void AnHdHomeSaysHd()
    {
        var title = Show(Offer(Northlight, OfferKind.Subscription, VideoQuality.Hd));
        Assert.Equal("On Northlight+ · HD", ContinueRowText.Where(title, [Northlight], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void AHomeTheViewerChoseWins()
    {
        var title = Show(Offer(Lampbox, OfferKind.Free, VideoQuality.Hd), Offer(Northlight, OfferKind.Subscription, VideoQuality.Uhd));
        var chosen = HomeOverrides.None.OpenThereInstead(Marsh, Lampbox);
        Assert.Equal("On Lampbox · free, with ads", ContinueRowText.Where(title, [Northlight, Lampbox], chosen, NameOf));
    }

    [Fact]
    public void ATitleOnNoneOfTheHouseholdsServicesHasNoWhereLine()
    {
        var title = Show(Offer(Northlight, OfferKind.Subscription, VideoQuality.Uhd));
        Assert.Null(ContinueRowText.Where(title, [Kite], HomeOverrides.None, NameOf));
    }

    [Fact]
    public void WithNothingCachedThereIsNoWhereLine() =>
        Assert.Null(ContinueRowText.Where(null, [Northlight], HomeOverrides.None, NameOf));

    [Fact]
    public void ThePictureIsNamedAsThePageDrawsIt()
    {
        Assert.Equal("4K", ContinueRowText.Quality(VideoQuality.Uhd));
        Assert.Equal("HD", ContinueRowText.Quality(VideoQuality.Hd));
        Assert.Equal("SD", ContinueRowText.Quality(VideoQuality.Sd));
    }

    [Fact]
    public void TheHomeIsTheOneTheWhereLineNames()
    {
        var title = Show(Offer(Lampbox, OfferKind.Free, VideoQuality.Hd), Offer(Northlight, OfferKind.Subscription, VideoQuality.Uhd));
        Assert.Equal(Northlight, ContinueRowText.Home(title, [Northlight, Lampbox], HomeOverrides.None)?.Service);
        Assert.Null(ContinueRowText.Home(title, [Kite], HomeOverrides.None));
        Assert.Null(ContinueRowText.Home(null, [Kite], HomeOverrides.None));
    }

    // ---- the action card's header (mockup 10) ----

    [Fact]
    public void TheCardsSecondLineIsWhatsNextAndTheService()
    {
        Assert.Equal("S1 · E3 · Slack Tide · Northlight+", ContinueRowText.CardSubtitle("S1 · E3 · Slack Tide", "Northlight+"));
        Assert.Equal("S1 · E3", ContinueRowText.CardSubtitle("S1 · E3", null));
        Assert.Equal("Northlight+", ContinueRowText.CardSubtitle(null, "Northlight+"));
        Assert.Null(ContinueRowText.CardSubtitle(null, null));
    }
}
