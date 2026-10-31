using Constitution.Domain.Recipes;

namespace Constitution.Tests.Recipes;

/// <summary>What a card is, and what it refuses to be.</summary>
public class RecipeTests
{
    private static Recipe Card(string title = "Lentil soup", string? source = null, int? servings = 4) =>
        new(new RecipeId("lentil-soup"), title, Cuisine.Other, [new Ingredient("1", "cup", "lentils")], [], servings, source, forKids: false);

    [Theory]
    [InlineData("Black-Bean")]
    [InlineData("two  words")]
    [InlineData("-lead")]
    [InlineData("trail-")]
    [InlineData("")]
    public void AnIdIsLowerCaseWordsAndDashes(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecipeId(value));
    }

    [Fact]
    public void AnIdOverTheLimitIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new RecipeId(new string('a', RecipeId.MaxLength + 1)));
        Assert.Equal(new string('a', RecipeId.MaxLength), new RecipeId(new string('a', RecipeId.MaxLength)).Value);
    }

    [Fact]
    public void TheTitleIsTrimmed()
    {
        Assert.Equal("Lentil soup", Card("  Lentil soup ").Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankTitleIsRefused(string title)
    {
        Assert.Throws<ArgumentException>(() => Card(title));
    }

    [Fact]
    public void ATitleOverTheLimitIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Card(new string('x', Recipe.MaxTitleLength + 1)));
        Assert.Equal(Recipe.MaxTitleLength, Card(new string('x', Recipe.MaxTitleLength)).Title.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(101)]
    public void ServingsAreOneToAHundred(int servings)
    {
        Assert.Throws<ArgumentException>(() => Card(servings: servings));
        Assert.Equal(100, Card(servings: 100).Servings);
        Assert.Equal(1, Card(servings: 1).Servings);
        Assert.Null(Card(servings: null).Servings);
    }

    [Theory]
    [InlineData("https://cooking.example.com/recipes/12")]
    [InlineData("http://example.com")]
    [InlineData("www.example.com")]
    [InlineData("cooking.example.com/recipes/12")]
    [InlineData("example.com")]
    public void ASourceLabelIsNeverAnAddress(string label)
    {
        Assert.Throws<ArgumentException>(() => Card(source: label));
    }

    [Theory]
    [InlineData("typed")]
    [InlineData("scanned")]
    [InlineData("Grandma's card")]
    [InlineData("Cooking magazine, March")]
    public void ALabelTheOwnerTypedIsKept(string label)
    {
        Assert.Equal(label, Card(source: label).SourceLabel);
    }

    [Fact]
    public void ABlankSourceLabelIsNoLabel()
    {
        Assert.Null(Card(source: "  ").SourceLabel);
    }

    [Fact]
    public void ALongSourceLabelIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Card(source: new string('s', Recipe.MaxSourceLabelLength + 1)));
    }

    [Fact]
    public void StandardLabelsAreTypedAndScanned()
    {
        Assert.Equal("typed", RecipeSource.Typed);
        Assert.Equal("scanned", RecipeSource.Scanned);
    }

    [Fact]
    public void ABlankIngredientNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Ingredient("1", "cup", "  "));
    }

    [Fact]
    public void IngredientPartsAreTrimmed()
    {
        var ingredient = new Ingredient(" 2 ", " ", " black beans ");

        Assert.Equal("2", ingredient.Quantity);
        Assert.Null(ingredient.Unit);
        Assert.Equal("black beans", ingredient.Name);
    }

    [Fact]
    public void ABlankStepIsRefusedAndMinutesAreBounded()
    {
        Assert.Throws<ArgumentException>(() => new RecipeStep(" ", null, true));
        Assert.Throws<ArgumentException>(() => new RecipeStep("Rest.", 0, false));
        Assert.Throws<ArgumentException>(() => new RecipeStep("Rest.", RecipeStep.MaxMinutes + 1, false));
        Assert.Equal(RecipeStep.MaxMinutes, new RecipeStep("Rest.", RecipeStep.MaxMinutes, false).Minutes);
    }

    [Fact]
    public void TooManyIngredientsOrStepsAreRefused()
    {
        var many = Enumerable.Range(0, Recipe.MaxLines + 1).Select(n => new Ingredient(null, null, "item " + n)).ToList();
        var steps = Enumerable.Range(0, Recipe.MaxLines + 1).Select(n => new RecipeStep("Step " + n, null, true)).ToList();

        Assert.Throws<ArgumentException>(() => new Recipe(new RecipeId("a"), "A", Cuisine.Other, many, [], null, null, false));
        Assert.Throws<ArgumentException>(() => new Recipe(new RecipeId("a"), "A", Cuisine.Other, [], steps, null, null, false));
    }

    [Fact]
    public void HandsOnMinutesCountActiveSteps()
    {
        Assert.Equal(10, RecipeData.Chili().HandsOnMinutes);
        Assert.Equal(0, Card().HandsOnMinutes);
    }

    [Fact]
    public void TwoCardsWithTheSameContentAreEqual()
    {
        Assert.Equal(RecipeData.Chili(), RecipeData.Chili());
        Assert.Equal(RecipeData.Chili().GetHashCode(), RecipeData.Chili().GetHashCode());
        Assert.NotEqual(RecipeData.Chili(), RecipeData.Chili() with { Cuisine = Cuisine.Other });
        Assert.NotEqual(RecipeData.Chili(), RecipeData.Chili() with { Ingredients = [new Ingredient(null, null, "water")] });
    }

    [Fact]
    public void EveryPartOfACardCountsTowardsEquality()
    {
        var chili = RecipeData.Chili();

        Assert.NotEqual(chili, chili with { Steps = [] });
        Assert.NotEqual(chili, chili with { Steps = [new RecipeStep("Soften the onion.", 11, true), .. chili.Steps.Skip(1)] });
        Assert.NotEqual(chili, chili with { Title = "Chili" });
        Assert.NotEqual(chili, chili with { Servings = 4 });
        Assert.NotEqual(chili, chili with { SourceLabel = RecipeSource.Scanned });
        Assert.NotEqual(chili, chili with { ForKids = false });
        Assert.NotEqual(chili, chili with { Id = new RecipeId("other-chili") });
    }

    [Fact]
    public void TheKidsFlagIsPartOfTheCard()
    {
        Assert.True(RecipeData.Chili().ForKids);
        Assert.False(Card().ForKids);
    }

    [Fact]
    public void AWithExpressionIsCaughtByEnsureValid()
    {
        var broken = Card() with { Servings = 0 };

        Assert.Throws<ArgumentException>(broken.EnsureValid);
    }
}
