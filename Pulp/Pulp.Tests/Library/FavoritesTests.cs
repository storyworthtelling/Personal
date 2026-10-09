using Pulp.Domain.Core;
using Pulp.Domain.Children;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// D230: a child's favorites come from the child's own star, a parent's pick and what the child
/// has watched most; each card says which, and only titles passing the child's filter appear.
/// The filter is the child's own (<see cref="ChildFilter"/>), run against the same
/// <see cref="ChildRules"/> that carries the parents' picks, so the two cannot be built apart (S4,
/// D255). Invented household.
/// </summary>
public class FavoritesTests
{
    private static readonly ViewerId ParentSam = new("parent-example-a");
    private static readonly TitleId Biscuit = new("biscuit-example");
    private static readonly TitleId Bugs = new("bugs-example");
    private static readonly TitleId Cloudberry = new("cloudberry-example");
    private static readonly TitleId Grim = new("grim-example");

    private static readonly ViewerId Wren = new("child-example-b");

    // Every invented title rated for the youngest band, unless a test says otherwise.
    private static TitleFacts AllAges(TitleId title) => new(title, TitleKind.Show, ContentRating.Parse("TV-Y"));

    private static ChildRules Child(params ParentPick[] picks) => new(Wren, AgeBand.SevenToNine) { ParentPicks = picks };

    [Fact]
    public void EachCardSaysWhereItCameFrom()
    {
        var cards = Favorites.For(
            Child(new ParentPick(Bugs, ParentSam, DaysAgo(2))),
            [new ChildStar(Biscuit, DaysAgo(1))],
            [new WatchTally(Tidewater, 9, DaysAgo(3))],
            watchedMostCount: 3,
            AllAges);

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
            Child(new ParentPick(Biscuit, ParentSam, DaysAgo(1)), new ParentPick(Bugs, ParentSam, DaysAgo(2))),
            [new ChildStar(Biscuit, DaysAgo(5))],
            [new WatchTally(Biscuit, 12, DaysAgo(1)), new WatchTally(Bugs, 8, DaysAgo(1))],
            watchedMostCount: 5,
            AllAges);

        Assert.Equal(2, cards.Count);
        Assert.Equal(FavoriteSource.ChildStar, cards.Single(c => c.Title == Biscuit).Source);
        Assert.Equal(FavoriteSource.ParentPick, cards.Single(c => c.Title == Bugs).Source);
    }

    [Fact]
    public void OnlyTheMostWatchedTitlesCountAsWatchedMost()
    {
        var cards = Favorites.For(
            Child(),
            [],
            [new WatchTally(Biscuit, 3, DaysAgo(1)), new WatchTally(Bugs, 9, DaysAgo(4)), new WatchTally(Cloudberry, 6, DaysAgo(2))],
            watchedMostCount: 2,
            AllAges);

        Assert.Equal([Bugs, Cloudberry], cards.Select(c => c.Title).OrderBy(t => t.Value));
    }

    [Fact]
    public void ATitleFailingTheFilterNeverAppearsFromAnySource()
    {
        // Grim is blocked for the child: a block beats even a parent's pick (D255).
        var child = Child(new ParentPick(Grim, ParentSam, DaysAgo(1))) with { BlockedTitles = new HashSet<TitleId> { Grim } };

        var cards = Favorites.For(
            child,
            [new ChildStar(Grim, DaysAgo(1))],
            [new WatchTally(Grim, 20, DaysAgo(1)), new WatchTally(Bugs, 1, DaysAgo(1))],
            watchedMostCount: 1,
            AllAges);

        // Grim neither appears nor takes the single watched-most place from Bugs.
        Assert.Equal([Bugs], cards.Select(c => c.Title));
    }

    [Fact]
    public void NewestFirst()
    {
        var cards = Favorites.For(
            Child(new ParentPick(Bugs, ParentSam, DaysAgo(1))),
            [new ChildStar(Biscuit, DaysAgo(9))],
            [new WatchTally(Cloudberry, 4, DaysAgo(5))],
            watchedMostCount: 1,
            AllAges);

        Assert.Equal([Bugs, Cloudberry, Biscuit], cards.Select(c => c.Title));
    }

    [Fact]
    public void AnUnwatchedTallyIsNotWatchedMost()
    {
        var cards = Favorites.For(Child(), [], [new WatchTally(Biscuit, 0, DaysAgo(1))], watchedMostCount: 3, AllAges);

        Assert.Empty(cards);
    }

    [Fact]
    public void ANegativeWatchedMostCountIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Favorites.For(Child(), [], [], watchedMostCount: -1, AllAges));
    }

    // S4 / D255: a parent's pick above the child's ceiling allow-lists it, so it shows on that
    // child's Favorites. The pick and the filter read one ChildRules, so they cannot drift apart.
    [Fact]
    public void AParentsPickAboveTheCeilingShowsOnTheChildsFavoritesThroughTheFilter()
    {
        var child = Child(new ParentPick(Grim, ParentSam, DaysAgo(1)));
        var grimFacts = new TitleFacts(Grim, TitleKind.Film, ContentRating.Parse("PG-13"));
        Assert.False(AgeBands.Allows(child.Ceiling, grimFacts.Rating));

        var cards = Favorites.For(child, [], [], watchedMostCount: 0, title => title == Grim ? grimFacts : AllAges(title));

        Assert.Equal([(Grim, FavoriteSource.ParentPick)], cards.Select(c => (c.Title, c.Source)));
        Assert.Equal(FilterReason.ParentFavorite, ChildFilter.Decide(child, grimFacts).Reason);
    }

    // The same title above the ceiling, starred by the child and never picked, stays off.
    [Fact]
    public void AStarAboveTheCeilingWithoutAParentsPickStaysOff()
    {
        var grimFacts = new TitleFacts(Grim, TitleKind.Film, ContentRating.Parse("PG-13"));

        var cards = Favorites.For(Child(), [new ChildStar(Grim, DaysAgo(1))], [], watchedMostCount: 0, title => grimFacts);

        Assert.Empty(cards);
    }

    // A title the catalogue has no facts for is hidden from the child, as an unrated one is (D128).
    [Fact]
    public void ATitleWithNoFactsIsHidden()
    {
        var cards = Favorites.For(Child(), [new ChildStar(Biscuit, DaysAgo(1))], [], watchedMostCount: 0, title => null);

        Assert.Empty(cards);
    }
}
