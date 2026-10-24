using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>Where the box comes from. Until the household file is wired the tab says so, and invents nothing.</summary>
public class RecipeBoxSourceTests
{
    [Fact]
    public void WithNoHouseholdFileTheSourceSaysSoInASentence()
    {
        var read = new NoRecipeBoxSource().Read();

        var none = Assert.IsType<RecipeBoxRead.NoHouseholdFile>(read);
        Assert.Equal("There is no household file on this phone yet, so the recipe box has no cards to show.", none.Sentence);
    }

    [Fact]
    public void WithNoHouseholdFileNothingCanBeMovedOrPlanned()
    {
        var source = new NoRecipeBoxSource();

        Assert.Throws<InvalidOperationException>(() => source.Move(new Constitution.Domain.Recipes.CuisineMove(new Constitution.Domain.Recipes.RecipeId("x"), Constitution.Domain.Recipes.Cuisine.Other, Constitution.Domain.Recipes.Cuisine.Asian)));
        Assert.Equal(CookSoonResult.NoPlannerYet, source.PlaceNextWeek(new Constitution.Domain.Recipes.RecipeId("x")));
    }

    [Fact]
    public void ASnapshotAnswersAMissingHistoryWithNever()
    {
        var snapshot = new RecipeBoxSnapshot(BoxFixture.Twelve(), new Dictionary<Constitution.Domain.Recipes.RecipeId, CardHistory>(), new HashSet<Constitution.Domain.Recipes.RecipeId>());

        Assert.Equal(CardHistory.Never, snapshot.HistoryOf(new Constitution.Domain.Recipes.RecipeId("black-bean-chili")));
    }
}
