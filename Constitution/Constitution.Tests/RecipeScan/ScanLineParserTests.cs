using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// Reading a scanned ingredient line into quantity, unit and name, and a step line into its minutes. The rule for both is the
/// same: read only what is exact, and leave the rest in the words. A quantity the parser cannot read stays in the name; a time
/// that could be two things is left out. Every line here is invented.
/// </summary>
public class ScanLineParserTests
{
    [Theory]
    [InlineData("6 apples, peeled and sliced", "6", null, "apples, peeled and sliced")]
    [InlineData("¾ cup brown sugar", "¾", "cup", "brown sugar")]
    [InlineData("1 1/2 cups flour", "1 1/2", "cups", "flour")]
    [InlineData("1½ tsp cinnamon", "1½", "tsp", "cinnamon")]
    [InlineData("1 ½ tbsp. honey", "1 ½", "tbsp", "honey")]
    [InlineData("0.75 lb ground turkey", "0.75", "lb", "ground turkey")]
    [InlineData("2 cans black beans, drained", "2", "cans", "black beans, drained")]
    [InlineData("1 cup of oats", "1", "cup", "oats")]
    [InlineData("  3 eggs  ", "3", null, "eggs")]
    [InlineData("1 cup (240 ml) milk", "1", "cup", "(240 ml) milk")]
    public void AReadableQuantityIsSplitOut(string line, string? quantity, string? unit, string name)
    {
        Assert.Equal(new ScanIngredientDraft(quantity, unit, name), ScanLineParser.Ingredient(line));
    }

    [Theory]
    [InlineData("salt, to taste")]
    [InlineData("a pinch of salt")]
    [InlineData("Zest of 1 lemon")]
    [InlineData("1-2 tsp cumin")]
    [InlineData("2 to 3 cloves garlic")]
    [InlineData("2 or 3 cloves garlic")]
    [InlineData("2")]
    [InlineData("1 cup")]
    [InlineData("0 eggs")]
    [InlineData("3x4 pan")]
    public void ANotQuiteReadableQuantityStaysInTheName(string line)
    {
        var draft = ScanLineParser.Ingredient(line);

        Assert.Null(draft.Quantity);
        Assert.Null(draft.Unit);
        Assert.Equal(line, draft.Name);
    }

    [Fact]
    public void ABlankLineKeepsAnEmptyName()
    {
        Assert.Equal(string.Empty, ScanLineParser.Ingredient("   ").Name);
    }

    [Theory]
    [InlineData("Bake 40 min, until bubbling", 40)]
    [InlineData("Simmer for 20 minutes.", 20)]
    [InlineData("Rest 1 minute", 1)]
    [InlineData("Bake 40 MIN", 40)]
    [InlineData("Heat to 350 degrees, bake 30 minutes", 30)]
    public void AnUnambiguousTimeIsRead(string line, int minutes)
    {
        var step = ScanLineParser.Step(line);

        Assert.Equal(minutes, step.Minutes);
        Assert.Equal(line, step.Text);
    }

    [Theory]
    [InlineData("Stir well.")]
    [InlineData("Oven to 350°F")]
    [InlineData("Bake 20-25 minutes")]
    [InlineData("Bake 20 to 25 minutes")]
    [InlineData("Bake 20 or 25 minutes")]
    [InlineData("Chill 1 hour")]
    [InlineData("Chill 30 minutes or 1 hour")]
    [InlineData("Boil 5 minutes, then simmer 10 minutes")]
    [InlineData("Add 5 minced onions")]
    [InlineData("Wait 0 minutes")]
    [InlineData("Wait 2000 minutes")]
    [InlineData("Rest a 20-minute spell")]
    public void AnAmbiguousTimeIsLeftOut(string line)
    {
        Assert.Null(ScanLineParser.Step(line).Minutes);
    }
}
