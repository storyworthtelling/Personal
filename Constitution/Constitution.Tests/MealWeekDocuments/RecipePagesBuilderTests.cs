using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>A cooked dinner's recipe document made from its card, the roster and the zone.</summary>
public class RecipePagesBuilderTests
{
    private static RecipePage Build(
        int day = 1, Recipe? card = null, IReadOnlyList<RosterEntry>? people = null, LastRatings? ratings = null, KidsMealFacts? kids = null)
    {
        var dinner = Week().Dinners[day];
        var chosen = card ?? Cards().Single(item => item.Id == dinner.Recipe);
        return RecipePagesBuilder.Build(dinner, chosen, people ?? People(), Zone, ratings, kids);
    }

    private static Recipe WithSteps(params RecipeStep[] steps) =>
        Cards()[1] with { Steps = steps };

    [Fact]
    public void TheCooksBlockIsTheDocumentsClock()
    {
        var page = Build();

        Assert.Equal(Sam, page.Model.Cook);
        Assert.Equal(new DateTime(2026, 10, 26, 18, 0, 0), page.Model.DinnerAt);
        Assert.Equal(new DateTime(2026, 10, 26, 17, 15, 0), page.Model.StartAt);
        Assert.Equal(new DateTime(2026, 10, 26), page.Model.Day);
        Assert.Equal("Lentil soup", page.Model.Title);
    }

    [Fact]
    public void TheHeadcountIsTheWholeRoster()
    {
        var page = Build();

        Assert.Equal(new RecipeHeadcount(2, 2, 0), page.Model.Headcount);
        Assert.Equal(4, page.Model.CardServings);
    }

    [Fact]
    public void QuantitiesAreScaledToWhoEats()
    {
        var card = Cards()[1] with { Servings = 2 };

        var text = TextOf(RecipeDocument.Build(Build(card: card).Model));

        Assert.Contains("2 cups", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardWithNoServingsIsPrintedAsWritten()
    {
        var page = Build(card: Cards()[1] with { Servings = null });

        Assert.Equal(4, page.Model.CardServings);
        Assert.Contains("1 cup", TextOf(RecipeDocument.Build(page.Model)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepsKeepTheCardsMinutes()
    {
        var model = Build().Model;

        Assert.Equal([15, 30], model.Steps!.Select(step => step.Minutes));
        Assert.Equal([true, false], model.Steps!.Select(step => step.Active));
        Assert.Equal(45, model.TotalMinutes);
        Assert.Equal(15, model.HandsOnMinutes);
    }

    [Fact]
    public void AStepWithNoMinutesTakesTheSmallestOnTheClock()
    {
        var card = WithSteps(new RecipeStep("Chop.", 20, true), new RecipeStep("Season to taste.", null, true));

        var model = Build(card: card).Model;

        Assert.Equal(RecipePagesBuilder.MissingStepMinutes, model.Steps![1].Minutes);
        Assert.Equal(1, RecipePagesBuilder.MissingStepMinutes);
        Assert.Equal(21, model.TotalMinutes);
        Assert.NotEmpty(RecipeDocument.Build(model));
    }

    [Fact]
    public void ACardOfNothingButWaitingLeavesHandsOnOut()
    {
        var model = Build(card: WithSteps(new RecipeStep("Bake.", 40, false))).Model;

        Assert.Null(model.HandsOnMinutes);
        Assert.NotEmpty(RecipeDocument.Build(model));
    }

    [Fact]
    public void ACardWithNoStepsIsRefusedPlainly()
    {
        var error = Assert.Throws<ArgumentException>(() => Build(card: WithSteps()));

        Assert.Contains("no steps", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstStepNamesTheCookBlockOnTheCalendar()
    {
        var model = Build().Model;

        Assert.Equal("Cook: Lentil soup", model.Steps![0].CalendarEvent);
        Assert.Null(model.Steps![1].CalendarEvent);
    }

    [Fact]
    public void NoKidsPageWithoutAKidsDinner()
    {
        var page = Build(day: 2);

        Assert.True(page.Dinner.ChildrenEatDifferently);
        Assert.Null(page.Model.KidsMeal);
        Assert.Equal(3, RecipeWithRating.Build(page.Model, page.Rating).Count);
    }

    [Fact]
    public void AKidsPageWhenTheChildrenEatDifferently()
    {
        var page = Build(day: 2, kids: new KidsMealFacts(KidsMeal(), Id("lentil-soup")));

        Assert.NotNull(page.Model.KidsMeal);
        Assert.True(page.Model.Steps![0].KidsHandOff);
        Assert.Equal(4, RecipeWithRating.Build(page.Model, page.Rating).Count);
    }

    [Fact]
    public void AKidsDinnerOnAnOrdinaryNightIsIgnored()
    {
        var page = Build(day: 1, kids: new KidsMealFacts(KidsMeal(), Id("lentil-soup")));

        Assert.Null(page.Model.KidsMeal);
    }

    [Fact]
    public void EveryEaterHasARatingRow()
    {
        var rating = Build().Rating;

        Assert.Equal([Sam, Lee, Robin, Juniper], rating.Rows.Select(row => row.Name));
        Assert.All(rating.Rows, row => Assert.Equal(Id("lentil-soup"), row.Recipe));
        Assert.All(rating.Rows, row => Assert.Equal("Lentil soup", row.Dish));
        Assert.Equal(Sam, rating.Cook);
        Assert.Equal(new DateTime(2026, 10, 26), rating.Day);
        Assert.Equal("sam", rating.Rows[0].Person.Value);
    }

    [Fact]
    public void AChildWhoAteTheKidsDinnerRatesThatDish()
    {
        var page = Build(day: 2, kids: new KidsMealFacts(KidsMeal(), Id("veggie-fried-rice")));

        var robin = page.Rating.Rows.Single(row => row.Name == Robin);
        var sam = page.Rating.Rows.Single(row => row.Name == Sam);
        Assert.Equal(Id("veggie-fried-rice"), robin.Recipe);
        Assert.Equal("Buttered noodles", robin.Dish);
        Assert.Equal(Id("sheet-pan-chicken"), sam.Recipe);
    }

    [Fact]
    public void TheLastRatingUnderlinesWhatTheProfileKnows()
    {
        var sam = new PersonTaste(
            new PersonId("sam"), false, [Id("lentil-soup")], [], [], new Dictionary<Cuisine, int>(), new Dictionary<string, decimal>(), new Dictionary<RecipeId, int>());
        var rows = Build(ratings: TasteLastRatings.From(new TasteProfile([sam], []))).Rating.Rows;

        Assert.Equal(TasteVerdict.Loved, rows.Single(row => row.Name == Sam).Last);
        Assert.Null(rows.Single(row => row.Name == Lee).Last);
    }

    [Fact]
    public void WithNoProfileEveryRowIsFirstTime()
    {
        Assert.All(Build().Rating.Rows, row => Assert.Null(row.Last));
    }

    [Fact]
    public void MoreEatersThanThePageHoldsIsRefusedNotTrimmed()
    {
        var five = People().Append(new RosterEntry(new PersonId("wren"), "Wren", MemberRole.Child, false, null)).ToList();

        var error = Assert.Throws<ArgumentException>(() => Build(people: five));

        Assert.Contains("4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADinnerThatIsNotCookedHasNoRecipe()
    {
        var out1 = Week().Dinners[5];

        Assert.Throws<ArgumentException>(() => RecipePagesBuilder.Build(out1, Cards()[0], People(), Zone, null, null));
    }

    [Fact]
    public void TheRatingRulesAreTheOnesTheReadBackIsGiven()
    {
        var page = Build();

        var expected = ExpectedDocument.ForRecipe(page.Model, page.Rating, Sam, page.Dinner.Day, Sam);

        Assert.Equal("Constitution — Recipe — 2026-10-26 — Sam", expected.Name);
        Assert.Single(expected.Pages);
        Assert.Equal(3, expected.Pages[0].Number);
    }
}
