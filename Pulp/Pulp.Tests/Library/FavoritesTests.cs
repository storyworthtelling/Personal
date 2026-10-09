using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// D230: a child's favorites come from the child's own star, a parent's pick and what the child
/// has watched most; each card says which, and only titles passing the child's filter appear.
/// The filter itself is another rule's; here it is a predicate. Invented household.
/// </summary>
public class FavoritesTests
{
    private static readonly ViewerId ParentSam = new("parent-example-a");
    private static readonly TitleId Biscuit = new("biscuit-example");
    private static readonly TitleId Bugs = new("bugs-example");
    private static readonly TitleId Cloudberry = new("cloudberry-example");
    private static readonly TitleId Grim = new("grim-example");

    private static bool PassesAll(TitleId title) => true;

    [Fact]
    public void EachCardSaysWhereItCameFrom()
    {
        var cards = Favorites.For(
            [new ChildStar(Biscuit, DaysAgo(1))],
            [new ParentPick(Bugs, ParentSam, DaysAgo(2))],
            [new WatchTally(Tidewater, 9, DaysAgo(3))],
            watchedMostCount: 3,
            PassesAll);

        Assert.Equal(
            [(Biscuit, FavoriteSource.ChildStar), (Bugs, FavoriteSource.ParentPick), (Tidewater, FavoriteSource.WatchedMost)],
            cards.Select(c => (c.Title, c.Source)));
        Assert.Equal(ParentSam, cards[1].PickedBy);
        Assert.Null(cards[0].PickedBy);
    }

    [Fact]
    public void ATitleFromSeveralSourcesIsOneCardAndTheChildsOwnStarWins()
    {
        var cards = Favorites.For(
            [new ChildStar(Biscuit, DaysAgo(5))],
            [new ParentPick(Biscuit, ParentSam, DaysAgo(1)), new ParentPick(Bugs, ParentSam, DaysAgo(2))],
            [new WatchTally(Biscuit, 12, DaysAgo(1)), new WatchTally(Bugs, 8, DaysAgo(1))],
            watchedMostCount: 5,
            PassesAll);

        Assert.Equal(2, cards.Count);
        Assert.Equal(FavoriteSource.ChildStar, cards.Single(c => c.Title == Biscuit).Source);
        Assert.Equal(FavoriteSource.ParentPick, cards.Single(c => c.Title == Bugs).Source);
    }

    [Fact]
    public void OnlyTheMostWatchedTitlesCountAsWatchedMost()
    {
        var cards = Favorites.For(
            [],
            [],
            [new WatchTally(Biscuit, 3, DaysAgo(1)), new WatchTally(Bugs, 9, DaysAgo(4)), new WatchTally(Cloudberry, 6, DaysAgo(2))],
            watchedMostCount: 2,
            PassesAll);

        Assert.Equal([Bugs, Cloudberry], cards.Select(c => c.Title).OrderBy(t => t.Value));
    }

    [Fact]
    public void ATitleFailingTheFilterNeverAppearsFromAnySource()
    {
        var cards = Favorites.For(
            [new ChildStar(Grim, DaysAgo(1))],
            [new ParentPick(Grim, ParentSam, DaysAgo(1))],
            [new WatchTally(Grim, 20, DaysAgo(1)), new WatchTally(Bugs, 1, DaysAgo(1))],
            watchedMostCount: 1,
            t => t != Grim);

        // Grim neither appears nor takes the single watched-most place from Bugs.
        Assert.Equal([Bugs], cards.Select(c => c.Title));
    }

    [Fact]
    public void NewestFirst()
    {
        var cards = Favorites.For(
            [new ChildStar(Biscuit, DaysAgo(9))],
            [new ParentPick(Bugs, ParentSam, DaysAgo(1))],
            [new WatchTally(Cloudberry, 4, DaysAgo(5))],
            watchedMostCount: 1,
            PassesAll);

        Assert.Equal([Bugs, Cloudberry, Biscuit], cards.Select(c => c.Title));
    }

    [Fact]
    public void AnUnwatchedTallyIsNotWatchedMost()
    {
        var cards = Favorites.For([], [], [new WatchTally(Biscuit, 0, DaysAgo(1))], watchedMostCount: 3, PassesAll);

        Assert.Empty(cards);
    }

    [Fact]
    public void ANegativeWatchedMostCountIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Favorites.For([], [], [], watchedMostCount: -1, PassesAll));
    }
}
