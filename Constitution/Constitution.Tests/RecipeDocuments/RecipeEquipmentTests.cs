using Constitution.Domain.Documents;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>C-2.5.1: the "Have out" list is derived from the steps and the ingredients, so the card never has to list it.</summary>
public class RecipeEquipmentTests
{
    private static IReadOnlyList<string> From(string[] steps, string[]? extra = null) =>
        RecipeEquipment.From(steps, RecipeSamples.Ingredients(), extra ?? []);

    [Fact]
    public void ReadsTheEquipmentTheStepsNeed()
    {
        var have = From(RecipeSamples.Steps());

        Assert.Equal(
            ["Large rimmed sheet pan", "Chef’s knife and board", "Large bowl", "Instant-read thermometer"], have);
    }

    [Fact]
    public void ADishThatNeedsNothingSpecialListsNothing()
    {
        Assert.Empty(From(["Stir everything together.", "Serve warm."]));
    }

    [Fact]
    public void AskedForTwiceItIsListedOnce()
    {
        var have = From(["Toss in a bowl.", "Whisk in another bowl."]);

        Assert.Equal(["Large bowl"], have);
    }

    [Fact]
    public void ExtraEquipmentFromTheCardFollowsTheDerivedOnesWithoutRepeats()
    {
        var have = From(["Halve the potatoes."], ["Salad spinner", "chef’s knife and board"]);

        Assert.Equal(["Chef’s knife and board", "Salad spinner"], have);
    }

    [Theory]
    [InlineData("Brown the onions in a skillet.", "Large skillet")]
    [InlineData("Bring a pot of water to the boil.", "Large pot")]
    [InlineData("Simmer in a saucepan.", "Medium saucepan")]
    [InlineData("Drain in a colander.", "Colander")]
    [InlineData("Purée in a blender.", "Blender")]
    [InlineData("Brown the beef in the Dutch oven.", "Dutch oven")]
    [InlineData("Lid on, low, in the slow cooker.", "Slow cooker")]
    public void RecognisesTheCommonVessels(string step, string expected)
    {
        Assert.Contains(expected, From([step]));
    }

    [Fact]
    public void AWordInsideAnotherWordIsNotEquipment()
    {
        Assert.Empty(From(["Add the bowling-green herbs.", "Taste for potash."]));
    }
}
