using Constitution.Domain.Groceries;

namespace Constitution.Tests.Groceries;

public class AisleTests
{
    [Fact]
    public void TheStoredWordsAreFrozen()
    {
        var words = Enum.GetValues<Aisle>().ToDictionary(aisle => aisle, Aisles.Words.Word);

        Assert.Equal(
            new Dictionary<Aisle, string>
            {
                [Aisle.Produce] = "produce",
                [Aisle.MeatAndFish] = "meat-fish",
                [Aisle.DairyAndEggs] = "dairy-eggs",
                [Aisle.Bakery] = "bakery",
                [Aisle.Frozen] = "frozen",
                [Aisle.CannedAndJarred] = "canned-jarred",
                [Aisle.DryGoods] = "dry-goods",
                [Aisle.BakingAndSpices] = "baking-spices",
                [Aisle.Other] = "other",
            },
            words);
    }

    [Fact]
    public void EveryAisleIsInTheShoppingOrderOnce()
    {
        Assert.Equal(Enum.GetValues<Aisle>().Length, Aisles.All.Count);
        Assert.Equal(Enum.GetValues<Aisle>().OrderBy(aisle => aisle), Aisles.All.OrderBy(aisle => aisle));
        Assert.Equal(Aisle.Produce, Aisles.All[0]);
        Assert.Equal(Aisle.Other, Aisles.All[^1]);
    }

    [Fact]
    public void EveryAisleHasALabel()
    {
        Assert.All(Aisles.All, aisle => Assert.False(string.IsNullOrWhiteSpace(Aisles.Label(aisle))));
        Assert.Equal("Meat and fish", Aisles.Label(Aisle.MeatAndFish));
    }

    [Fact]
    public void OnlyFreshFoodSpoils()
    {
        var perishable = Aisles.All.Where(Aisles.IsPerishable).ToList();

        Assert.Equal([Aisle.Produce, Aisle.MeatAndFish, Aisle.DairyAndEggs, Aisle.Bakery], perishable);
    }

    [Theory]
    [InlineData("onion", Aisle.Produce)]
    [InlineData("Yellow onions", Aisle.Produce)]
    [InlineData("bell pepper", Aisle.Produce)]
    [InlineData("black pepper", Aisle.BakingAndSpices)]
    [InlineData("chicken thighs", Aisle.MeatAndFish)]
    [InlineData("parmesan", Aisle.DairyAndEggs)]
    [InlineData("eggs", Aisle.DairyAndEggs)]
    [InlineData("coconut milk", Aisle.CannedAndJarred)]
    [InlineData("milk", Aisle.DairyAndEggs)]
    [InlineData("peanut butter", Aisle.CannedAndJarred)]
    [InlineData("butter", Aisle.DairyAndEggs)]
    [InlineData("butternut squash", Aisle.Produce)]
    [InlineData("crushed tomatoes", Aisle.CannedAndJarred)]
    [InlineData("tomatoes", Aisle.Produce)]
    [InlineData("frozen peas", Aisle.Frozen)]
    [InlineData("black beans", Aisle.CannedAndJarred)]
    [InlineData("spaghetti", Aisle.DryGoods)]
    [InlineData("breadcrumbs", Aisle.DryGoods)]
    [InlineData("bread", Aisle.Bakery)]
    [InlineData("flour", Aisle.BakingAndSpices)]
    [InlineData("olive oil", Aisle.BakingAndSpices)]
    [InlineData("dragon fruit powder", Aisle.Other)]
    public void AnIngredientIsLookedForWhereItIsSold(string name, Aisle expected)
    {
        Assert.Equal(expected, Aisles.Of(name));
    }

    [Fact]
    public void ANameIsKeyedWithoutCaseSpacesOrAPluralS()
    {
        Assert.Equal(IngredientNames.Key("onion"), IngredientNames.Key("  Onions "));
        Assert.Equal(IngredientNames.Key("olive oil"), IngredientNames.Key("Olive   OIL"));
        Assert.NotEqual(IngredientNames.Key("rice"), IngredientNames.Key("rice flour"));
        Assert.Equal(IngredientNames.Key("molasses"), IngredientNames.Key("molasses"));
        Assert.Equal("glass", IngredientNames.Key("glass"));
    }
}
