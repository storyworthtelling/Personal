using Pulp.Domain.Discover;

namespace Pulp.Tests.Discover;

/// <summary>
/// A pile is a literal stack (D219): one poster at the front with the rest fanned behind, a swipe to
/// flip to the next, and "1 of 3" saying where you are. The swipe is read here, from the drag's
/// distances, so the page's touch handler only reports what the finger did.
/// </summary>
public class DiscoverPagePileTests
{
    [Theory]
    [InlineData(0, 3, "1 of 3")]
    [InlineData(2, 3, "3 of 3")]
    [InlineData(0, 1, "1 of 1")]
    public void ThePositionReadsOneOfN(int index, int count, string expected) =>
        Assert.Equal(expected, DiscoverPagePile.Position(index, count));

    [Fact]
    public void SwipingPastTheLastCardComesRoundToTheFirstAndBackwards()
    {
        Assert.Equal(1, DiscoverPagePile.Next(0, 3));
        Assert.Equal(0, DiscoverPagePile.Next(2, 3));
        Assert.Equal(2, DiscoverPagePile.Previous(0, 3));
        Assert.Equal(0, DiscoverPagePile.Next(0, 1));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(9, 2)]
    public void AtMostTwoCardsAreFannedBehindTheFront(int count, int behind) =>
        Assert.Equal(behind, DiscoverPagePile.FannedBehind(count));

    [Fact]
    public void ALeftDragPastTheThresholdIsTheNextCardARightOneThePrevious()
    {
        Assert.Equal(DiscoverSwipe.Next, DiscoverPagePile.Swipe(-120, 10, 80));
        Assert.Equal(DiscoverSwipe.Previous, DiscoverPagePile.Swipe(120, -10, 80));
    }

    [Fact]
    public void AShortOrMostlyVerticalDragIsNotASwipeSoThePageStillScrolls()
    {
        Assert.Equal(DiscoverSwipe.None, DiscoverPagePile.Swipe(-40, 0, 80));
        Assert.Equal(DiscoverSwipe.None, DiscoverPagePile.Swipe(-120, 200, 80));
    }

    [Fact]
    public void AnEmptyPileHasNoPositionToGiveOrCardToTurnTo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoverPagePile.Position(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoverPagePile.Next(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoverPagePile.Position(3, 3));
    }
}
