using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>The pile (C-6.2.1, book p.13, D56-D58): cards overlapping at loose angles, every title readable, no tape.</summary>
public class PileLayoutTests
{
    private const int Well = 320;

    private static Pile Italian() => new RecipeBox(BoxFixture.Twelve()).Piles.Single(pile => pile.Cuisine == Cuisine.Italian);

    [Fact]
    public void EveryTitleStaysReadable()
    {
        foreach (var width in new[] { 280, 320, 411 })
        {
            var spread = PileLayout.Place(Italian(), width, null);

            for (var index = 0; index < spread.Cards.Count - 1; index++)
            {
                var visibleBand = spread.Cards[index + 1].TopDp - spread.Cards[index].TopDp;
                Assert.True(
                    visibleBand - 2 * PileLayout.RiseDp(width) >= PileLayout.ReadableTitleDp,
                    "Card " + index + " at width " + width + " leaves " + visibleBand + "dp");
            }
        }
    }

    [Fact]
    public void CardsOverlapRatherThanSitApart()
    {
        var spread = PileLayout.Place(Italian(), Well, null);

        for (var index = 0; index < spread.Cards.Count - 1; index++)
        {
            Assert.True(spread.Cards[index + 1].TopDp < spread.Cards[index].TopDp + spread.Cards[index].HeightDp);
        }
    }

    [Fact]
    public void AnglesAreLooseBoundedAndTheSameEveryTime()
    {
        var first = PileLayout.Place(Italian(), Well, null);
        var second = PileLayout.Place(Italian(), Well, null);

        Assert.Equal(first.Cards.Select(card => card.AngleDegrees), second.Cards.Select(card => card.AngleDegrees));
        Assert.All(first.Cards, card => Assert.InRange(card.AngleDegrees, -PileLayout.MaxAngleDegrees, PileLayout.MaxAngleDegrees));
        Assert.True(first.Cards.Select(card => card.AngleDegrees).Distinct().Count() > 1, "A pile of straight cards is a list");
    }

    [Fact]
    public void EveryCardStaysInsideTheWell()
    {
        var spread = PileLayout.Place(Italian(), Well, null);

        Assert.All(spread.Cards, card =>
        {
            Assert.True(card.LeftDp >= 0);
            Assert.True(card.LeftDp + card.WidthDp <= Well);
        });
    }

    [Fact]
    public void ThePileIsAsTallAsItsLastCardEnds()
    {
        var spread = PileLayout.Place(Italian(), Well, null);
        var last = spread.Cards[^1];

        Assert.Equal(last.TopDp + last.HeightDp, spread.HeightDp);
        Assert.Equal(Italian().Cards.Select(card => card.Id), spread.Cards.Select(card => card.Card.Id));
    }

    [Fact]
    public void ThePickedCardLeavesItsGapInThePile()
    {
        var pile = Italian();
        var picked = pile.Cards[1].Id;

        var spread = PileLayout.Place(pile, Well, picked);
        var plain = PileLayout.Place(pile, Well, null);

        Assert.Equal(pile.Count, spread.Cards.Count);
        Assert.Equal([picked], spread.Cards.Where(card => card.IsGap).Select(card => card.Card.Id));
        Assert.Equal(plain.HeightDp, spread.HeightDp);
        Assert.Equal(plain.Cards.Select(card => card.TopDp), spread.Cards.Select(card => card.TopDp));
    }

    [Fact]
    public void ACardInAnotherPileMakesNoGap()
    {
        var spread = PileLayout.Place(Italian(), Well, new RecipeId("peanut-noodles"));

        Assert.DoesNotContain(spread.Cards, card => card.IsGap);
    }

    [Fact]
    public void ALoneCardIsStillAPile()
    {
        var pile = new RecipeBox(BoxFixture.Twelve()).Piles.Single(candidate => candidate.Cuisine == Cuisine.Other);

        var spread = PileLayout.Place(pile, Well, null);

        Assert.Single(spread.Cards);
        Assert.Equal(PileLayout.CardHeightDp, spread.HeightDp);
    }
}
