using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Moq;

namespace Constitution.Tests.MealTasteSource;

public class TasteDishNamesTests
{
    private static Recipe Card(string id, string title) => Constitution.Tests.Recipes.RecipeData.Plain(id, title, Cuisine.Italian, "pasta");

    private static IRecipeBoxSource Box(RecipeBoxRead read)
    {
        var box = new Mock<IRecipeBoxSource>();
        box.Setup(source => source.Read()).Returns(read);
        return box.Object;
    }

    [Fact]
    public void ACardIsNamedByItsTitle()
    {
        var names = new TasteDishNames(Box(new RecipeBoxRead.Ready(new RecipeBoxSnapshot([Card("tomato-pasta", "Tomato pasta")], new Dictionary<RecipeId, CardHistory>(), new HashSet<RecipeId>()))));

        Assert.Equal("Tomato pasta", names.Of(new RecipeId("tomato-pasta")));
        Assert.Null(names.Of(new RecipeId("gone")));
    }

    [Fact]
    public void ABoxThatCannotBeReadNamesNothingAndDoesNotThrow()
    {
        var names = new TasteDishNames(Box(new RecipeBoxRead.Unavailable("The household file cannot be read.")));

        Assert.Null(names.Of(new RecipeId("tomato-pasta")));
    }

    [Fact]
    public void TheBoxIsReadOnceForManyCards()
    {
        var box = new Mock<IRecipeBoxSource>();
        box.Setup(source => source.Read()).Returns(new RecipeBoxRead.Ready(new RecipeBoxSnapshot([Card("a", "A")], new Dictionary<RecipeId, CardHistory>(), new HashSet<RecipeId>())));
        var names = new TasteDishNames(box.Object);

        names.Of(new RecipeId("a"));
        names.Of(new RecipeId("a"));

        box.Verify(source => source.Read(), Times.Once);
    }
}
