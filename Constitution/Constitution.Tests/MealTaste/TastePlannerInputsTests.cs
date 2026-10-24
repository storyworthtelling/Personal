using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Tests.MealPlanning;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The adapter from the profile to what the committed dinner planner already accepts. The planner itself is not touched.</summary>
public class TastePlannerInputsTests
{
    private static TastePlannerInputs Inputs(IEnumerable<TasteRating>? ratings = null, IEnumerable<TasteNote>? notes = null)
    {
        var box = Box();
        return TastePlannerInputs.From(TasteProfile.Build(People(), box, ratings ?? [], notes ?? []), People(), box);
    }

    [Fact]
    public void NeverAgainIsKeyedByCardAndNamesThePeople()
    {
        var inputs = Inputs([Rate(Sam, Risotto, TasteVerdict.NeverAgain), Rate(Robin, Tacos, TasteVerdict.NeverAgain), Rate(Lee, Risotto, TasteVerdict.NeverAgain)]);

        Assert.Equal(["Lee", "Sam"], inputs.NeverAgain[Risotto].Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["Robin"], inputs.NeverAgain[Tacos].ToList());
        Assert.Equal(2, inputs.NeverAgain.Count);
    }

    [Fact]
    public void ALaterLovedLiftsTheNeverAgain()
    {
        var inputs = Inputs([Rate(Sam, Risotto, TasteVerdict.NeverAgain, 0), Rate(Sam, Risotto, TasteVerdict.Loved, 10)]);

        Assert.Empty(inputs.NeverAgain);
    }

    [Fact]
    public void ASeedDislikeIsNotAHardBlock()
    {
        var inputs = Inputs(notes: TasteSeed.Notes(Sam, ["mushrooms", "Mexican"], Day(0)));

        Assert.Empty(inputs.NeverAgain);
    }

    [Fact]
    public void NoProfileMeansNoInputs()
    {
        var inputs = TastePlannerInputs.From(TasteProfile.Empty, People(), Box());

        Assert.Empty(inputs.NeverAgain);
        Assert.All(inputs.Appetite.Values, value => Assert.Equal(0m, value));
        Assert.Equal(Box().Count, inputs.Appetite.Count);
    }

    [Fact]
    public void ApplyAddsToWhatTheRequestAlreadyHolds()
    {
        var own = new Dictionary<RecipeId, IReadOnlySet<string>>
        {
            [Curry] = new HashSet<string> { "Lee" },
            [Risotto] = new HashSet<string> { "Lee" },
        };
        var request = MealData.Request(recipes: Box(), neverAgain: own, seed: 7);

        var applied = Inputs([Rate(Sam, Risotto, TasteVerdict.NeverAgain), Rate(Robin, Tacos, TasteVerdict.NeverAgain)]).Apply(request);

        Assert.Equal(["Lee", "Sam"], applied.NeverAgain[Risotto].Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["Lee"], applied.NeverAgain[Curry].ToList());
        Assert.Equal(["Robin"], applied.NeverAgain[Tacos].ToList());
        Assert.Equal(7, applied.Seed);
        Assert.Equal(request.WeekStart, applied.WeekStart);
        Assert.Same(request.Recipes, applied.Recipes);
        Assert.Equal(["Lee"], request.NeverAgain[Risotto].ToList());
    }

    [Fact]
    public void ApplyMatchesNamesWithoutCase()
    {
        var own = new Dictionary<RecipeId, IReadOnlySet<string>> { [Risotto] = new HashSet<string> { "sam" } };
        var request = MealData.Request(recipes: Box(), neverAgain: own);

        var applied = Inputs([Rate(Sam, Risotto, TasteVerdict.NeverAgain)]).Apply(request);

        Assert.Single(applied.NeverAgain[Risotto]);
    }

    [Fact]
    public void ApplyingNothingChangesNothing()
    {
        var request = MealData.Request(recipes: Box());

        var applied = Inputs().Apply(request);

        Assert.Empty(applied.NeverAgain);
        Assert.Equal(request.Seed, applied.Seed);
    }

    [Fact]
    public void ThePlannerHonoursATasteNeverAgainWithoutBeingEdited()
    {
        var request = MealData.Request();
        var ratings = MealData.Box().Select(card => Rate(Sam, card.Id, TasteVerdict.NeverAgain)).Where(rating => rating.Recipe.Value is "mushroom-risotto" or "lentil-curry" or "corn-tacos").ToList();
        var inputs = TastePlannerInputs.From(TasteProfile.Build(People(), request.Recipes, ratings, []), People(), request.Recipes);

        var applied = inputs.Apply(request);
        var week = MealPlanner.Plan(applied);

        Assert.True(week.IsPlanned);
        Assert.Empty(MealRules.Violations(applied, week));
        Assert.DoesNotContain(week.Dinners, dinner => dinner.Recipe is { } id && ratings.Any(rating => rating.Recipe == id));
    }

    [Fact]
    public void AChildsNeverAgainLetsTheChildrenEatDifferently()
    {
        var request = MealData.Request(childrenHome: MealData.AllEvenings());
        var inputs = TastePlannerInputs.From(
            TasteProfile.Build(People(), request.Recipes, [Rate(Robin, new RecipeId("tomato-pasta"), TasteVerdict.NeverAgain)], []), People(), request.Recipes);

        var applied = inputs.Apply(request);
        var week = MealPlanner.Plan(applied);

        Assert.Empty(MealRules.Violations(applied, week));
        Assert.All(week.Dinners.Where(dinner => dinner.Recipe == new RecipeId("tomato-pasta")), dinner => Assert.True(dinner.ChildrenEatDifferently));
    }

    [Fact]
    public void AppetiteIsTheHouseholdsSumOverEveryone()
    {
        var inputs = Inputs([
            Rate(Sam, Curry, TasteVerdict.Loved), Rate(Lee, Curry, TasteVerdict.Fine), Rate(Robin, Curry, TasteVerdict.NeverAgain),
        ]);

        Assert.Equal(TasteWeights.Loved + TasteWeights.Fine + TasteWeights.NeverAgain, inputs.Appetite[Curry]);
    }

    [Fact]
    public void RankedPutsTheMostWantedFirstAndTheNeverAgainLast()
    {
        var inputs = Inputs([Rate(Sam, Tart, TasteVerdict.Loved), Rate(Lee, Tart, TasteVerdict.Loved), Rate(Sam, Chicken, TasteVerdict.Loved), Rate(Sam, Curry, TasteVerdict.NeverAgain)]);

        var ranked = inputs.Ranked(Box()).Select(card => card.Id).ToList();

        Assert.Equal(Tart, ranked[0]);
        Assert.Equal(Chicken, ranked[1]);
        Assert.Equal(Curry, ranked[^1]);
    }

    [Fact]
    public void WithNothingToGoOnRankedIsJustIdOrder()
    {
        var ranked = Inputs().Ranked(Box().AsEnumerable().Reverse()).Select(card => card.Id.Value).ToList();

        Assert.Equal(ranked.OrderBy(id => id, StringComparer.Ordinal).ToList(), ranked);
    }

    [Fact]
    public void ACardTheProfileNeverHeardOfScoresZero()
    {
        var inputs = Inputs([Rate(Sam, Tart, TasteVerdict.Loved)]);
        var stranger = TasteData.Dish(new RecipeId("moon-soup"), Cuisine.Other, false, "cheese");

        var ranked = inputs.Ranked(new[] { stranger, Box().Single(card => card.Id == Tart) }).Select(card => card.Id).ToList();

        Assert.Equal(new[] { Tart, stranger.Id }, ranked);
    }
}
