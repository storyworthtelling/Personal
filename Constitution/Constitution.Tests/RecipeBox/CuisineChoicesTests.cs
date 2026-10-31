using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>The action card a long-press on a card raises (C-6.2.4): the cuisines, the current one greyed with its reason.</summary>
public class CuisineChoicesTests
{
    [Fact]
    public void TheCardOffersEveryCuisineInOrderWithTheCatchAllLast()
    {
        var card = CuisineChoices.For(BoxFixture.Chili());

        Assert.Equal(Cuisines.All, card.Rows.Select(row => row.Cuisine));
        Assert.Equal(Cuisine.Other, card.Rows[^1].Cuisine);
    }

    [Fact]
    public void TheCurrentCuisineIsGreyedWithAReasonAndNotHidden()
    {
        var row = CuisineChoices.For(BoxFixture.Chili()).Rows.Single(candidate => candidate.Cuisine == Cuisine.Mexican);

        Assert.False(row.IsEnabled);
        Assert.Equal("Filed here now", row.Reason);
        Assert.Equal(7, CuisineChoices.For(BoxFixture.Chili()).Rows.Count(candidate => candidate.IsEnabled));
    }

    [Fact]
    public void TheHeadingNamesTheCard()
    {
        var card = CuisineChoices.For(BoxFixture.Chili());

        Assert.Equal("Move to another cuisine", card.Title);
        Assert.Equal("Black bean chili", card.Subtitle);
    }
}
