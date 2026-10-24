using Constitution.Domain.Recipes;

namespace Constitution.Tests.Recipes;

/// <summary>The box as piles (C-6.2.1, D53, D60) and the search under the switcher (D59).</summary>
public class RecipeBoxTests
{
    private static RecipeBox Full() => new(RecipeData.Box());

    private static List<Cuisine> Order(IReadOnlyList<Pile> piles) => piles.Select(pile => pile.Cuisine).ToList();

    [Fact]
    public void ThereIsOnePilePerCuisineThatHasCards()
    {
        var piles = Full().Piles;

        Assert.Equal(5, piles.Count);
        Assert.Equal(piles.Count, piles.Select(pile => pile.Cuisine).Distinct().Count());
        Assert.DoesNotContain(piles, pile => pile.Cuisine == Cuisine.French);
    }

    [Fact]
    public void EveryCardIsInExactlyOnePile()
    {
        var box = Full();

        Assert.Equal(10, box.Count);
        Assert.Equal(3, box.Piles.Single(pile => pile.Cuisine == Cuisine.Mexican).Count);
        Assert.Equal(1, box.Piles.Single(pile => pile.Cuisine == Cuisine.American).Count);
        Assert.Equal(box.Count, box.Piles.Sum(pile => pile.Count));
    }

    [Fact]
    public void BiggerPilesFirstAndOtherLast()
    {
        Assert.Equal([Cuisine.Italian, Cuisine.Mexican, Cuisine.Asian, Cuisine.American, Cuisine.Other], Order(Full().Piles));
    }

    [Fact]
    public void OtherIsLastEvenWhenItIsTheBiggest()
    {
        var cards = new List<Recipe>
        {
            RecipeData.Plain("a", "A", Cuisine.Other), RecipeData.Plain("b", "B", Cuisine.Other), RecipeData.Plain("c", "C", Cuisine.Other),
            RecipeData.Plain("d", "D", Cuisine.French),
        };

        Assert.Equal([Cuisine.French, Cuisine.Other], Order(new RecipeBox(cards).Piles));
    }

    [Fact]
    public void CardsReadByTitleIgnoringAccents()
    {
        var cards = new List<Recipe>
        {
            RecipeData.Plain("c", "crème caramel", Cuisine.French),
            RecipeData.Plain("a", "Apple tart", Cuisine.French),
            RecipeData.Plain("e", "Éclair", Cuisine.French),
            RecipeData.Plain("b", "Bouillabaisse", Cuisine.French),
        };

        var titles = new RecipeBox(cards).Piles.Single().Cards.Select(card => card.Title);

        Assert.Equal(["Apple tart", "Bouillabaisse", "crème caramel", "Éclair"], titles);
    }

    [Fact]
    public void TheGivenOrderDoesNotMatter()
    {
        var forward = new RecipeBox(RecipeData.Box());
        var backward = new RecipeBox(Enumerable.Reverse(RecipeData.Box()));

        Assert.Equal(
            forward.Piles.SelectMany(pile => pile.Cards.Select(card => card.Id.Value)),
            backward.Piles.SelectMany(pile => pile.Cards.Select(card => card.Id.Value)));
        Assert.Equal(Order(forward.Piles), Order(backward.Piles));
    }

    [Fact]
    public void AnEmptyBoxHasNoPiles()
    {
        var box = new RecipeBox([]);

        Assert.Equal(0, box.Count);
        Assert.Empty(box.Piles);
    }

    [Fact]
    public void TwoCardsWithOneIdAreRefused()
    {
        var twin = new List<Recipe> { RecipeData.Chili(), RecipeData.Chili() with { Title = "Chili again" } };

        Assert.Throws<ArgumentException>(() => new RecipeBox(twin));
    }

    // ---- search ----

    [Theory]
    [InlineData("lasagna")]
    [InlineData("LASAGNA")]
    [InlineData("  lasagna  ")]
    [InlineData("lasag")]
    public void SearchFindsATitleWithoutCaseOrPadding(string query)
    {
        var found = Full().Search(query);

        Assert.Equal("Weeknight lasagna", Assert.Single(found.SelectMany(pile => pile.Cards)).Title);
    }

    [Theory]
    [InlineData("jalapeno")]
    [InlineData("JALAPEÑO")]
    [InlineData("jalapeño")]
    public void SearchIgnoresAccentsBothWays(string query)
    {
        Assert.Equal("Sheet-pan fajitas", Assert.Single(Full().Search(query).SelectMany(pile => pile.Cards)).Title);
    }

    [Fact]
    public void SearchIgnoresAnAccentOnTheTitleSide()
    {
        var box = new RecipeBox([RecipeData.Plain("c", "Crème caramel", Cuisine.French)]);

        Assert.Single(box.Search("creme").SelectMany(pile => pile.Cards));
    }

    [Fact]
    public void SearchReadsIngredientNames()
    {
        var found = Full().Search("ricotta").SelectMany(pile => pile.Cards);

        Assert.Equal("Weeknight lasagna", Assert.Single(found).Title);
    }

    [Fact]
    public void SearchDoesNotReadQuantitiesUnitsOrSteps()
    {
        var card = new Recipe(
            new RecipeId("soup"), "Soup", Cuisine.Other, [new Ingredient("7", "tablespoons", "water")],
            [new RecipeStep("Whisk vigorously.", null, true)], null, null, false);
        var box = new RecipeBox([card]);

        Assert.Empty(box.Search("tablespoons"));
        Assert.Empty(box.Search("7"));
        Assert.Empty(box.Search("whisk"));
        Assert.Single(box.Search("water"));
    }

    [Fact]
    public void EveryQueryWordMustMatch()
    {
        var box = Full();

        Assert.Equal(["Sheet-pan chicken, potatoes"], box.Search("chicken potatoes").SelectMany(pile => pile.Cards).Select(card => card.Title));
        Assert.Equal(["Chicken tinga tacos", "Sheet-pan chicken, potatoes"], box.Search("chicken").SelectMany(pile => pile.Cards).Select(card => card.Title).Order(StringComparer.Ordinal));
        Assert.Empty(box.Search("chicken ricotta"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoQueryShowsTheWholeBox(string? query)
    {
        Assert.Equal(Full().Piles.Select(pile => pile.Count), Full().Search(query).Select(pile => pile.Count));
    }

    [Fact]
    public void SearchNarrowsInPlace()
    {
        var box = Full();

        // The full box orders Italian before Asian; a narrowed view keeps that order.
        var found = box.Search("peas");

        Assert.Equal([Cuisine.Italian, Cuisine.Asian], Order(found));
        Assert.All(found, pile => Assert.Equal(1, pile.Count));
    }

    [Fact]
    public void PilesWithNoMatchGo()
    {
        var found = Full().Search("chili");

        Assert.Equal([Cuisine.Mexican], Order(found));
        Assert.Equal(["Black bean chili"], found.Single().Cards.Select(card => card.Title));
    }

    [Fact]
    public void ANoMatchIsAnEmptyListNotAnError()
    {
        Assert.Empty(Full().Search("kohlrabi"));
    }

    [Fact]
    public void TheFindLabelCountsTheCards()
    {
        Assert.Equal("Find a recipe · 23 cards", RecipeBoxText.FindLabel(23));
        Assert.Equal("Find a recipe · 1 card", RecipeBoxText.FindLabel(1));
        Assert.Equal("Find a recipe · 0 cards", RecipeBoxText.FindLabel(0));
    }
}
