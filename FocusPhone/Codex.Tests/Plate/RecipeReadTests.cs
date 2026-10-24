using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Reading a recipe card out of recognised text (PBI C-11.2.1). Every recipe here is invented.
///
/// <para>Like a business card, this errs toward leaving a field empty: the owner confirms the
/// words in Constitution before anything is kept, and a wrong line that looks right is one
/// nobody re-reads.</para>
/// </summary>
public class RecipeReadTests
{
    private static readonly string[] Lentil =
    [
        "Marigold Lentil Stew",
        "Serves 4  -  Prep 15 min",
        "INGREDIENTS",
        "1 cup red lentils",
        "2 tbsp olive oil",
        "1 onion, finely chopped",
        "salt and pepper, to taste",
        "DIRECTIONS",
        "1. Warm the oil in a heavy pot over",
        "medium heat.",
        "2. Add the onion and cook until soft.",
        "3. Stir in the lentils and 4 cups water, then",
        "simmer for 25 minutes.",
    ];

    [Fact]
    public void AnOrdinaryCardGivesUpAllThreeParts()
    {
        var reading = RecipeRead.Read(Lentil);

        Assert.Equal("Marigold Lentil Stew", reading.Title);
        Assert.Equal(
            ["1 cup red lentils", "2 tbsp olive oil", "1 onion, finely chopped", "salt and pepper, to taste"],
            reading.Ingredients);
        Assert.Equal(3, reading.Steps.Count);
    }

    [Fact]
    public void WrappedStepIsOneStep()
    {
        var reading = RecipeRead.Read(Lentil);

        Assert.Equal("Warm the oil in a heavy pot over medium heat.", reading.Steps[0]);
        Assert.Equal("Stir in the lentils and 4 cups water, then simmer for 25 minutes.", reading.Steps[2]);
    }

    [Fact]
    public void ServingsLineIsNoPartOfIt()
    {
        var reading = RecipeRead.Read(Lentil);

        Assert.DoesNotContain(reading.Ingredients, line => line.Contains("Serves", StringComparison.Ordinal));
        Assert.DoesNotContain(reading.Steps, line => line.Contains("Serves", StringComparison.Ordinal));
        Assert.DoesNotContain("Serves", reading.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIngredientThatWrapsIsRejoined()
    {
        var reading = RecipeRead.Read([
            "Quince Oat Bars",
            "Ingredients:",
            "2 tbsp butter, softened and",
            "cut into small cubes",
            "1 cup rolled oats",
            "Method:",
            "Mix everything together and press into a tin.",
        ]);

        Assert.Equal(["2 tbsp butter, softened and cut into small cubes", "1 cup rolled oats"], reading.Ingredients);
        Assert.Equal(["Mix everything together and press into a tin."], reading.Steps);
    }

    [Theory]
    [InlineData("Directions")]
    [InlineData("METHOD")]
    [InlineData("Instructions:")]
    [InlineData("Preparation")]
    [InlineData("Steps")]
    public void EveryCommonHeadingStartsTheSteps(string heading)
    {
        var reading = RecipeRead.Read([
            "Plum Toast", "Ingredients", "2 plums", "4 slices bread", heading, "Slice the plums. Toast the bread.",
        ]);

        Assert.Equal(["2 plums", "4 slices bread"], reading.Ingredients);
        Assert.Equal(["Slice the plums. Toast the bread."], reading.Steps);
    }

    [Fact]
    public void UnnumberedStepsBreakAtFullStops()
    {
        var reading = RecipeRead.Read([
            "Plum Toast", "Ingredients", "2 plums", "Directions",
            "Slice the plums and lay them",
            "over the bread.",
            "Grill until the edges darken.",
        ]);

        Assert.Equal(["Slice the plums and lay them over the bread.", "Grill until the edges darken."], reading.Steps);
    }

    [Fact]
    public void ASecondPagePicksUpWhereTheFirstStopped()
    {
        // The activity hands over every page's lines, in order. Page two has no heading of its
        // own and carries on numbering; a repeated heading must not wipe what page one gave.
        var pageOne = Lentil.Take(Lentil.Length - 2).ToList();
        string[] pageTwo = ["DIRECTIONS", "3. Stir in the lentils and 4 cups water.", "4. Season and serve."];

        var reading = RecipeRead.Read([.. pageOne, .. pageTwo]);

        Assert.Equal(4, reading.Ingredients.Count);
        Assert.Equal(4, reading.Steps.Count);
        Assert.Equal("Season and serve.", reading.Steps[3]);
    }

    [Fact]
    public void HeadinglessReadsQuantityLines()
    {
        var reading = RecipeRead.Read([
            "Pear Fool",
            "3 ripe pears",
            "1/2 cup cream",
            "Poach the pears until tender, then mash them with the cream.",
        ]);

        Assert.Equal(["3 ripe pears", "1/2 cup cream"], reading.Ingredients);
        Assert.Equal(["Poach the pears until tender, then mash them with the cream."], reading.Steps);
        Assert.Equal("Pear Fool", reading.Title);
    }

    [Fact]
    public void AVulgarFractionCountsAsAQuantity()
    {
        var reading = RecipeRead.Read(["Pear Fool", "½ cup cream", "2 pears"]);

        Assert.Equal(["½ cup cream", "2 pears"], reading.Ingredients);
    }

    [Theory]
    [InlineData("Thank you for visiting")]
    [InlineData("Open daily from nine until five")]
    public void TextThatIsNotARecipeReadsAsNothing(string line)
    {
        var reading = RecipeRead.Read([line, "Follow us for news", "www.example.com"]);

        Assert.True(reading.IsEmpty);
        Assert.Equal(string.Empty, reading.Title);
    }

    [Fact]
    public void LoneNumberIsAPageNumber()
    {
        var reading = RecipeRead.Read(["Plum Toast", "Ingredients", "2 plums", "14", "Directions", "Toast it."]);

        Assert.Equal(["2 plums"], reading.Ingredients);
    }

    [Fact]
    public void NullAndBlankInputReadAsNothing()
    {
        Assert.True(RecipeRead.Read(null!).IsEmpty);
        Assert.True(RecipeRead.Read([]).IsEmpty);
        Assert.True(RecipeRead.Read(["", "   "]).IsEmpty);
    }

    [Fact]
    public void ALongLineIsNeverTakenForATitle()
    {
        var reading = RecipeRead.Read([
            "This card was written by hand for the whole family to use at every holiday dinner",
            "Ingredients",
            "2 plums",
        ]);

        Assert.Equal(string.Empty, reading.Title);
        Assert.Equal(["2 plums"], reading.Ingredients);
    }
}
