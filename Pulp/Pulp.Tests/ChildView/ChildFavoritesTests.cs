using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// A child's Favorites page as the shell builds it (mockup 06, D230): the three sources for each
/// child watching, never a title some child watching may not see (D280), each poster's small line
/// saying which source, and the star the child adds or takes away (D253). Invented household.
/// </summary>
public class ChildFavoritesTests
{
    private static TitleFacts? AllAges(TitleId title) => title == Grim ? Rated(title, "TV-MA") : Rated(title, "TV-Y");

    private static WatchEvent Sitting(TitleId title, int daysAgo, WatchSource source = WatchSource.Session) =>
        new(title, WatchedKind.Show, DaysAgo(daysAgo), new ServiceId("streamly-example"), Evidence: source);

    [Fact]
    public void TheThreeSourcesMakeThePage()
    {
        var robin = RobinRules() with { ParentPicks = [new ParentPick(Bugs, Parent, DaysAgo(2))] };
        var cards = ChildFavorites.For(
            ChildAudience.Of([robin]),
            child => [new ChildStar(Biscuit, DaysAgo(1))],
            child => [Sitting(Tidewater, 3), Sitting(Tidewater, 4)],
            AllAges);

        Assert.Equal(new[] { Biscuit, Bugs, Tidewater }, cards.Select(card => card.Title));
        Assert.Equal(new[] { FavoriteSource.ChildStar, FavoriteSource.ParentPick, FavoriteSource.WatchedMost }, cards.Select(card => card.Card.Source));
    }

    [Fact]
    public void TogetherATitleOneChildMayNotSeeIsNotOnThePage()
    {
        var robin = RobinRules(AgeBand.TenToTwelve);
        var stars = new Dictionary<ViewerId, ChildStar[]> { [Robin] = [new ChildStar(Bugs, DaysAgo(1))], [Sam] = [] };
        var facts = new Dictionary<TitleId, TitleFacts> { [Bugs] = Rated(Bugs, "TV-PG") };

        var cards = ChildFavorites.For(ChildAudience.Of([robin, SamRules(AgeBand.UnderSeven)]), child => stars[child], child => [], id => facts.GetValueOrDefault(id));

        Assert.Empty(cards);
    }

    [Fact]
    public void TogetherEachChildsFavoritesAreOnThePageOnce()
    {
        var stars = new Dictionary<ViewerId, ChildStar[]>
        {
            [Robin] = [new ChildStar(Biscuit, DaysAgo(2))],
            [Sam] = [new ChildStar(Biscuit, DaysAgo(1)), new ChildStar(Cloudberry, DaysAgo(3))],
        };

        var cards = ChildFavorites.For(ChildAudience.Of([RobinRules(), SamRules()]), child => stars[child], child => [], AllAges);

        Assert.Equal(new[] { Biscuit, Cloudberry }, cards.Select(card => card.Title));
        Assert.Equal(Robin, cards[0].Child);
    }

    [Fact]
    public void AHiddenTitleTakesNoWatchedMostPlace()
    {
        var cards = ChildFavorites.For(
            ChildAudience.Of([RobinRules()]),
            child => [],
            child => [Sitting(Grim, 1), Sitting(Grim, 2), Sitting(Grim, 3), Sitting(Tidewater, 4)],
            AllAges);

        Assert.Equal(new[] { Tidewater }, cards.Select(card => card.Title));
    }

    [Fact]
    public void AMarkedUnwatchedSittingIsNotAWatch()
    {
        var tallies = ChildFavorites.Tallies([Sitting(Tidewater, 1), Sitting(Tidewater, 2, WatchSource.MarkedUnwatched), Sitting(Biscuit, 3)]);

        Assert.Equal(1, tallies.Single(tally => tally.Title == Tidewater).Times);
        Assert.Equal(DaysAgo(1), tallies.Single(tally => tally.Title == Tidewater).LastAt);
    }

    [Fact]
    public void AGameIsNotAFavorite() =>
        Assert.Empty(ChildFavorites.Tallies([new WatchEvent(Tidewater, WatchedKind.Game, DaysAgo(1), null)]));

    [Theory]
    [InlineData(FavoriteSource.ChildStar, "★ Robin's star")]
    [InlineData(FavoriteSource.ParentPick, "Picked by parent-example-a")]
    [InlineData(FavoriteSource.WatchedMost, "Watched most")]
    public void EachPosterSaysWhereItCameFrom(FavoriteSource source, string line)
    {
        var card = new ChildFavoriteCard(
            new FavoriteCard(Biscuit, source, source == FavoriteSource.ParentPick ? Parent : null, DaysAgo(1)), Robin);

        Assert.Equal(line, ChildFavorites.SourceLine(card, id => id.Value));
    }

    [Fact]
    public void APickWithNoParentNamedSaysAParent() =>
        Assert.Equal("Picked by a parent", ChildFavorites.SourceLine(
            new ChildFavoriteCard(new FavoriteCard(Biscuit, FavoriteSource.ParentPick, null, DaysAgo(1)), Robin), id => id.Value));

    [Fact]
    public void StarringTogetherStarsItForEveryChildWatching()
    {
        var entries = ChildFavorites.ToStar(ChildAudience.Of([RobinRules(), SamRules()]), child => [], Biscuit, Now);

        Assert.Equal(new[] { Robin, Sam }, entries.Select(entry => entry.Child));
        Assert.All(entries, entry => Assert.Equal(Biscuit, entry.Star.Title));
    }

    [Fact]
    public void AStarAlreadyThereIsNotWrittenAgain()
    {
        var stars = new Dictionary<ViewerId, ChildStar[]> { [Robin] = [new ChildStar(Biscuit, DaysAgo(5))], [Sam] = [] };

        var entries = ChildFavorites.ToStar(ChildAudience.Of([RobinRules(), SamRules()]), child => stars[child], Biscuit, Now);

        Assert.Equal(new[] { Sam }, entries.Select(entry => entry.Child));
    }

    [Fact]
    public void UnstarringTakesAwayEveryWatchingChildsStarAndOnlyTheirs()
    {
        var stars = new Dictionary<ViewerId, ChildStar[]>
        {
            [Robin] = [new ChildStar(Biscuit, DaysAgo(5)), new ChildStar(Cloudberry, DaysAgo(2))],
        };

        var audience = ChildAudience.Of([RobinRules()]);
        var entries = ChildFavorites.ToUnstar(audience, child => stars.GetValueOrDefault(child) ?? [], Biscuit);

        Assert.Equal(new[] { new ChildStarEntry(Robin, new ChildStar(Biscuit, DaysAgo(5))) }, entries);
        Assert.True(ChildFavorites.IsStarred(audience, child => stars.GetValueOrDefault(child) ?? [], Biscuit));
        Assert.False(ChildFavorites.IsStarred(audience, child => stars.GetValueOrDefault(child) ?? [], Tidewater));
    }
    [Theory]
    [InlineData(OfferKind.Subscription, "On Streamly")]
    [InlineData(OfferKind.Free, "On Streamly · free")]
    public void ThePostersWhereLineNamesTheServiceAndSaysFree(OfferKind kind, string line)
    {
        var service = new ServiceId("streamly-example");
        var title = new CatalogueTitle
        {
            Id = Biscuit,
            Kind = TitleKind.Show,
            Name = "Biscuit the Brave",
            Offers = [new TitleOffer(service, "Streamly", true, kind, VideoQuality.Hd, null, null, "https://example.com/biscuit", null, null, false)],
        };

        Assert.Equal(line, ChildFavorites.WhereLine(title, new Home(service, VideoQuality.Hd, WithAds: kind == OfferKind.Free), "Streamly"));
    }
}
