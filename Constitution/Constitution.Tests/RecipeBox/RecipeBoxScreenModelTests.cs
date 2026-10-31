using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>The Meals tab's recipe box as a model (C-6.2.1, C-6.2.5): piles and search, or ten places while it is short.</summary>
public class RecipeBoxScreenModelTests
{
    [Fact]
    public void SearchNarrowsInPlace()
    {
        var whole = RecipeBoxScreenModel.Build(BoxFixture.Twelve(), null);
        var narrowed = RecipeBoxScreenModel.Build(BoxFixture.Twelve(), "chili");

        Assert.Equal(["Mexican", "American"], narrowed.Piles.Select(pile => pile.Label));
        Assert.Equal(whole.Piles.Select(pile => pile.Label).Where(label => label is "Mexican" or "American"), narrowed.Piles.Select(pile => pile.Label));
        Assert.Equal(["Black bean chili"], narrowed.Piles[0].Cards.Select(card => card.Title));
        Assert.Equal(["Turkey chili mac"], narrowed.Piles[1].Cards.Select(card => card.Title));
    }

    [Fact]
    public void TheFindLabelCountsTheWholeBoxEvenWhileSearching()
    {
        Assert.Equal("Find a recipe · 12 cards", RecipeBoxScreenModel.Build(BoxFixture.Twelve(), "salmon").FindLabel);
    }

    [Fact]
    public void AMatchlessSearchSaysSoAndDropsNoCard()
    {
        var model = RecipeBoxScreenModel.Build(BoxFixture.Twelve(), "zzz");

        Assert.Empty(model.Piles);
        Assert.Equal("No card in the box matches that.", model.NothingMatches);
        Assert.Null(RecipeBoxScreenModel.Build(BoxFixture.Twelve(), null).NothingMatches);
    }

    [Fact]
    public void TenCardsShowThePilesAndNineShowTheEmptyBox()
    {
        var ten = RecipeBoxScreenModel.Build(RecipeData10(), null);
        var nine = RecipeBoxScreenModel.Build(RecipeData10().Take(9).ToList(), null);

        Assert.Equal(BoxMode.Piles, ten.Mode);
        Assert.True(ten.ShowsSearch);
        Assert.Equal("Add a recipe", ten.PrimaryAction);
        Assert.Equal(BoxMode.EmptyBox, nine.Mode);
        Assert.False(nine.ShowsSearch);
        Assert.Empty(nine.Piles);
    }

    [Fact]
    public void TheEmptyBoxCountsToTenWithTheTitlesInTheFilledPlaces()
    {
        var model = RecipeBoxScreenModel.Build(RecipeData10().Take(3).ToList(), null);

        Assert.NotNull(model.Empty);
        Assert.Equal("3 of 10", model.Empty!.State.Counter);
        Assert.Equal(10, model.Empty.PlaceTitles.Count);
        Assert.Equal(3, model.Empty.PlaceTitles.Count(title => title is not null));
        Assert.All(model.Empty.PlaceTitles.Skip(3), title => Assert.Null(title));
        Assert.Equal("Scan a recipe card", model.PrimaryAction);
        Assert.Equal("Ten family favourites, and the planning can begin.", model.Empty.State.Invitation);
    }

    [Fact]
    public void PlanningWaitsForTen()
    {
        Assert.False(RecipeBoxScreenModel.Build(RecipeData10().Take(9).ToList(), null).Empty!.State.PlanningMayStart);
        Assert.Null(RecipeBoxScreenModel.Build(RecipeData10(), null).Empty);
    }

    [Fact]
    public void TheInvitationIsNotAVoiceSeat()
    {
        var model = RecipeBoxScreenModel.Build(RecipeData10().Take(2).ToList(), null);

        Assert.True(model.Empty!.InvitationIsStaticCopy);
        Assert.Equal(RecipeBoxText.Invitation, model.Empty.State.Invitation);
    }

    [Fact]
    public void ABoxWithNoCardsIsAnEmptyBoxNotAnError()
    {
        var model = RecipeBoxScreenModel.Build([], null);

        Assert.Equal(BoxMode.EmptyBox, model.Mode);
        Assert.Equal("0 of 10", model.Empty!.State.Counter);
    }

    [Fact]
    public void ThePileLabelsAreTheCuisineNames()
    {
        Assert.Equal(["Italian", "Mexican", "Asian", "American", "Other"], RecipeBoxScreenModel.Build(RecipeData10(), null).Piles.Select(pile => pile.Label));
    }

    private static List<Recipe> RecipeData10() => Constitution.Tests.Recipes.RecipeData.Box();
}
