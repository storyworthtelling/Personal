using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Tests.MealPlacement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Groceries.GroceryData;

namespace Constitution.Tests.Groceries;

/// <summary>C-6.4.2: the plan's ingredients, minus what is on hand, by store section. Every dish and figure is invented.</summary>
public class GroceryListTests
{
    private static GroceryList ListFor(MealWeek week, Pantry? pantry = null, int headcount = 4) =>
        GroceryList.From(week, Cards, pantry ?? Pantry.Empty, headcount);

    private static List<string> Names(GroceryList list) => list.Lines.Select(line => line.Name).ToList();

    // ---- merging ----

    [Fact]
    public void TheSameIngredientIsOneLine()
    {
        var list = ListFor(ThreeDinners());

        Assert.Equal("2 cups", Line(list, "rice").Quantity);
        Assert.Equal("3", Line(list, "onion").Quantity);
        Assert.Equal("2 tbsp", Line(list, "olive oil").Quantity);
        Assert.Single(list.Lines, line => line.Name.Contains("rice", StringComparison.Ordinal));
    }

    [Fact]
    public void ALineWithNoQuantityMergesAndStaysBare()
    {
        var list = ListFor(ThreeDinners());

        Assert.Equal(string.Empty, Line(list, "salt").Quantity);
    }

    [Fact]
    public void UnlikeUnitsStayOnSeparateLines()
    {
        var cup = Card("milk-a", "Milk a", 4, Item("1", "cup", "milk"));
        var spoons = Card("milk-b", "Milk b", 4, Item("2", "tbsp", "milk"));
        var week = Cooking((0, cup), (1, spoons));

        var list = GroceryList.From(week, [cup, spoons], Pantry.Empty, 4);

        var milk = list.Lines.Where(line => line.Name == "milk").Select(line => line.Quantity).Order().ToList();
        Assert.Equal(["1 cup", "2 tbsp"], milk);
    }

    [Fact]
    public void UnitSpellingsThatAreOneUnitAreAdded()
    {
        var first = Card("oil-a", "Oil a", 4, Item("1", "tablespoon", "olive oil"));
        var second = Card("oil-b", "Oil b", 4, Item("2", "Tbsp.", "olive oil"));

        var list = GroceryList.From(Cooking((0, first), (1, second)), [first, second], Pantry.Empty, 4);

        Assert.Equal("3 tablespoons", Line(list, "olive oil").Quantity);
    }

    [Fact]
    public void AQuantityAndABareLineAreNotAdded()
    {
        var some = Card("salt-a", "Salt a", 4, Item("1", "tsp", "salt"));
        var bare = Card("salt-b", "Salt b", 4, Item(null, null, "salt"));

        var list = GroceryList.From(Cooking((0, some), (1, bare)), [some, bare], Pantry.Empty, 4);

        Assert.Equal(["", "1 tsp"], list.Lines.Where(line => line.Name == "salt").Select(line => line.Quantity).Order().ToList());
    }

    [Fact]
    public void AnUnreadableQuantityIsKeptAsTheCardHasIt()
    {
        var spicy = Card("spice-a", "Spice a", 4, Item("1-2", "tsp", "cumin"), Item("a pinch", null, "chili flakes"));
        var again = Card("spice-b", "Spice b", 4, Item("1", "tsp", "cumin"), Item("a pinch", null, "chili flakes"));

        var list = GroceryList.From(Cooking((0, spicy), (1, again)), [spicy, again], Pantry.Empty, 6);

        Assert.Equal(["1-2 tsp", "1½ tsp"], list.Lines.Where(line => line.Name == "cumin").Select(line => line.Quantity).Order().ToList());
        Assert.Equal(2, list.Lines.Count(line => line.Name == "chili flakes"));
        Assert.All(list.Lines.Where(line => line.Name == "chili flakes"), line => Assert.Equal("a pinch", line.Quantity));
    }

    [Fact]
    public void TwoSpellingsOfOnePluralAreOneLine()
    {
        var list = ListFor(ThreeDinners());

        Assert.Single(list.Lines, line => IngredientNames.Key(line.Name) == IngredientNames.Key("onion"));
    }

    // ---- scaling ----

    [Fact]
    public void QuantitiesScaleToTheHeadcount()
    {
        var list = ListFor(Cooking((0, Chili)), headcount: 6);

        Assert.Equal("1½ cups", Line(list, "rice").Quantity);
        Assert.Equal("3 cans", Line(list, "black beans").Quantity);
    }

    [Fact]
    public void ACardWithNoServingsIsTakenAsWritten()
    {
        var loose = Card("loose", "Loose", null, Item("2", "cups", "flour"));

        var list = GroceryList.From(Cooking((0, loose)), [loose], Pantry.Empty, 9);

        Assert.Equal("2 cups", Line(list, "flour").Quantity);
    }

    [Fact]
    public void ADinnerTwiceIsBoughtForTwice()
    {
        var list = ListFor(Cooking((0, Chili), (3, Chili)));

        Assert.Equal("4 cans", Line(list, "black beans").Quantity);
    }

    // ---- minus stock, by section ----

    [Fact]
    public void MinusStockBySection()
    {
        var pantry = Pantry.From([OnHand("rice", Aisle.DryGoods), OnHand("salt", Aisle.BakingAndSpices, standing: true)]);

        var list = ListFor(ThreeDinners(), pantry);

        Assert.DoesNotContain("rice", Names(list));
        Assert.DoesNotContain("salt", Names(list));
        Assert.Equal(
            [Aisle.Produce, Aisle.MeatAndFish, Aisle.DairyAndEggs, Aisle.CannedAndJarred, Aisle.DryGoods, Aisle.BakingAndSpices],
            list.Sections.Select(section => section.Aisle).ToList());
        Assert.Equal(["garlic", "lemon", "onion"], list.Sections[0].Lines.Select(line => line.Name).ToList());
        Assert.Equal(["black beans", "crushed tomatoes"], list.Sections[3].Lines.Select(line => line.Name).ToList());
        Assert.Equal(["olive oil"], list.Sections[5].Lines.Select(line => line.Name).ToList());
    }

    [Fact]
    public void AnEmptyAisleHasNoSection()
    {
        var list = ListFor(Cooking((0, Pasta)));

        Assert.DoesNotContain(list.Sections, section => section.Aisle == Aisle.Bakery);
        Assert.All(list.Sections, section => Assert.NotEmpty(section.Lines));
    }

    [Fact]
    public void AStaplesAisleBeatsTheKeywordTable()
    {
        var pantry = Pantry.From([Out("olive oil", Aisle.CannedAndJarred)]);

        var list = ListFor(Cooking((0, Pasta)), pantry);

        Assert.Contains(list.Sections.Single(section => section.Aisle == Aisle.CannedAndJarred).Lines, line => line.Name == "olive oil");
    }

    // ---- staples ----

    [Fact]
    public void StaplesReorder()
    {
        var pantry = Pantry.From(
        [
            Out("olive oil", Aisle.BakingAndSpices),
            Out("honey", Aisle.BakingAndSpices),
            OnHand("salt", Aisle.BakingAndSpices, standing: true),
        ]);

        var list = ListFor(Cooking((0, Pasta)), pantry);

        var oil = Line(list, "olive oil");
        Assert.True(oil.IsStaple);
        Assert.Equal("1 tbsp", oil.Quantity);
        var honey = Line(list, "honey");
        Assert.True(honey.IsStaple);
        Assert.Equal(string.Empty, honey.Quantity);
        Assert.Equal(Aisle.BakingAndSpices, honey.Aisle);
        Assert.DoesNotContain("salt", Names(list));
    }

    [Fact]
    public void ABlankStapleMeansNoNeed()
    {
        var pantry = Pantry.From([OnHand("honey", Aisle.BakingAndSpices, standing: true), OnHand("cumin", Aisle.BakingAndSpices, standing: true)]);

        var list = ListFor(Cooking((3, Pasta)), pantry);

        Assert.DoesNotContain("honey", Names(list));
        Assert.DoesNotContain("cumin", Names(list));
    }

    [Fact]
    public void AStapleThatIsOutIsListedOnceNotTwice()
    {
        var pantry = Pantry.From([Out("rice", Aisle.DryGoods)]);

        var list = ListFor(ThreeDinners(), pantry);

        var rice = Assert.Single(list.Lines, line => line.Name == "rice");
        Assert.Equal("2 cups", rice.Quantity);
        Assert.True(rice.IsStaple);
    }

    // ---- the week that was not cooked ----

    [Fact]
    public void UncookedWritesOffPerishables()
    {
        var lastWeek = Cooking((0, Chili), (1, Pasta));
        var pantry = Pantry.From(
        [
            OnHand("garlic", Aisle.Produce),
            OnHand("parmesan", Aisle.DairyAndEggs, standing: true),
            OnHand("onion", Aisle.Produce),
        ]);

        var after = pantry.AfterUncooked(lastWeek, new HashSet<DateOnly> { Monday.AddDays(1) }, Cards);

        Assert.Null(after.Find("garlic"));
        Assert.True(after.Find("parmesan")!.NeedMore);
        Assert.True(after.Find("parmesan")!.Standing);
        Assert.NotNull(after.Find("onion"));
        Assert.False(after.Find("onion")!.NeedMore);
    }

    [Fact]
    public void UncookedAssumesTheRestInStock()
    {
        var lastWeek = Cooking((0, Chili), (1, Pasta));

        var after = Pantry.Empty.AfterUncooked(lastWeek, new HashSet<DateOnly> { Monday.AddDays(1) }, Cards);

        Assert.All(["spaghetti", "crushed tomatoes", "olive oil"], name =>
        {
            var item = after.Find(name)!;
            Assert.False(item.NeedMore);
            Assert.False(item.Standing);
        });
        Assert.Null(after.Find("rice"));
        Assert.Null(after.Find("parmesan"));
        Assert.Null(after.Find("garlic"));
    }

    [Fact]
    public void UncookedChangesTheNextList()
    {
        var lastWeek = Cooking((1, Pasta));
        var pantry = Pantry.From([OnHand("garlic", Aisle.Produce), Out("honey", Aisle.BakingAndSpices)]);

        var after = pantry.AfterUncooked(lastWeek, new HashSet<DateOnly> { Monday.AddDays(1) }, Cards);
        var list = ListFor(Cooking((0, Pasta)), after);

        Assert.Contains("garlic", Names(list));
        Assert.DoesNotContain("spaghetti", Names(list));
        Assert.DoesNotContain("olive oil", Names(list));
        Assert.Contains("honey", Names(list));
    }

    [Fact]
    public void ACookedDinnerIsLeftAlone()
    {
        var lastWeek = Cooking((0, Chili));
        var pantry = Pantry.From([OnHand("onion", Aisle.Produce)]);

        var after = pantry.AfterUncooked(lastWeek, new HashSet<DateOnly>(), Cards);

        Assert.Equal(pantry.Items, after.Items);
    }

    [Fact]
    public void ADayThatWasNeverACookedDinnerIsIgnored()
    {
        var lastWeek = Cooking((0, Chili));

        var after = Pantry.Empty.AfterUncooked(lastWeek, new HashSet<DateOnly> { Monday.AddDays(4) }, Cards);

        Assert.Empty(after.Items);
    }

    // ---- what is not a cooked dinner ----

    [Fact]
    public void LeftoversAndEatingOutAddNothing()
    {
        var week = MealWeek.Planned(
            Monday,
            Enumerable.Range(0, MealWeek.Evenings).Select(offset => offset % 2 == 0
                ? DinnerChoice.EatOut(Monday.AddDays(offset), "Out.")
                : DinnerChoice.Leftovers(Monday.AddDays(offset), "Leftovers.")));

        Assert.True(ListFor(week).IsEmpty);
    }

    [Fact]
    public void AWeekNotPlannedIsAnEmptyList()
    {
        var week = MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "The box is too small.");

        var list = ListFor(week);

        Assert.True(list.IsEmpty);
        Assert.Empty(list.Lines);
    }

    [Fact]
    public void ACardMissingFromTheBoxIsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() => GroceryList.From(ThreeDinners(), [Chili], Pantry.Empty, 4));

        Assert.Contains("Tuesday", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AHeadcountHasToBeSomeone(int headcount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ListFor(ThreeDinners(), headcount: headcount));
    }
}
