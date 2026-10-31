using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Tests.MealTaste;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealRanking;

/// <summary>The taste score as a ranking input to the dinner planner: it orders cards that already pass every rule, and never admits one.</summary>
public class TasteRankingTests
{
    // Frozen from the planner as it was before the input existed (240 invented requests, hashed week by week).
    private const ulong TodaysWeeks = 7827400281381623395UL;

    private static List<Recipe> Italian(int count) =>
        Enumerable.Range(0, count).Select(index => Dish("dish-" + (char)('a' + index), Cuisine.Italian)).ToList();

    private static IReadOnlyDictionary<RecipeId, decimal> Liking(IReadOnlyList<Recipe> cards) =>
        cards.Select((card, index) => (card.Id, Score: (decimal)(cards.Count - index))).ToDictionary(pair => pair.Id, pair => pair.Score);

    [Fact]
    public void NoOpinionIsTodaysWeek()
    {
        Assert.Equal(TodaysWeeks, PlannerFingerprint.Of(PlannerFingerprint.RequestFor));
    }

    [Fact]
    public void EmptyOrZeroOrEvenScoresAreTodaysWeek()
    {
        Assert.Equal(TodaysWeeks, PlannerFingerprint.Of(seed => PlannerFingerprint.RequestFor(seed) with { Appetite = new Dictionary<RecipeId, decimal>() }));
        Assert.Equal(TodaysWeeks, PlannerFingerprint.Of(seed =>
        {
            var request = PlannerFingerprint.RequestFor(seed);
            return request with { Appetite = request.Recipes.ToDictionary(card => card.Id, _ => 0m) };
        }));
        Assert.Equal(TodaysWeeks, PlannerFingerprint.Of(seed =>
        {
            var request = PlannerFingerprint.RequestFor(seed);
            return request with { Appetite = request.Recipes.ToDictionary(card => card.Id, _ => 7.5m) };
        }));
    }

    [Fact]
    public void ARealScoreChangesSomeWeeks()
    {
        var ranked = PlannerFingerprint.Of(seed =>
        {
            var request = PlannerFingerprint.RequestFor(seed);
            return request with { Appetite = request.Recipes.Select((card, index) => (card.Id, Score: (decimal)index)).ToDictionary(pair => pair.Id, pair => pair.Score) };
        });

        Assert.NotEqual(TodaysWeeks, ranked);
    }

    [Fact]
    public void CardsTiedOnEverythingElseFollowTheirScore()
    {
        var cards = Italian(12);
        var request = Request(recipes: cards, seed: 3) with { Appetite = Liking(cards) };

        var week = MealPlanner.Plan(request);

        var stretch = Cooks(week).Single(dinner => dinner.IsStretch);
        Assert.Equal(cards[0].Id, stretch.Recipe);
        var rest = Cooks(week).Where(dinner => !dinner.IsStretch).OrderBy(dinner => dinner.Day).Select(dinner => request.Appetite[dinner.Recipe!.Value]).ToList();
        Assert.True(rest.Count >= 4);
        Assert.Equal(rest.OrderByDescending(score => score).ToList(), rest);
    }

    [Fact]
    public void TheLeastLikedCardGoesLast()
    {
        var cards = Italian(12);
        var request = Request(recipes: cards, seed: 3) with { Appetite = Liking(cards) };

        var cooked = Cooks(MealPlanner.Plan(request)).Select(dinner => dinner.Recipe).ToHashSet();

        Assert.DoesNotContain(cards[^1].Id, cooked);
        Assert.Contains(cards[0].Id, cooked);
    }

    [Fact]
    public void AScoreOnlyOrdersTies()
    {
        // The most liked card was cooked three weeks ago; eleven cards never were, and seven evenings are all they are needed for.
        var cards = Italian(12);
        var request = Request(recipes: cards, seed: 3, history: [new CookedMeal(cards[0].Id, Sunday.AddDays(-20))]) with { Appetite = Liking(cards) };

        var cooked = Cooks(MealPlanner.Plan(request)).Select(dinner => dinner.Recipe).ToHashSet();

        Assert.DoesNotContain(cards[0].Id, cooked);
    }

    [Fact]
    public void ADishSomeoneNeverWantsAgainStaysOut()
    {
        var cards = Italian(12);
        var request = Request(recipes: cards, seed: 3, neverAgain: Never("dish-a", "Sam", "Lee")) with { Appetite = Liking(cards) };

        var week = MealPlanner.Plan(request);

        Assert.DoesNotContain(Cooks(week), dinner => dinner.Recipe == cards[0].Id);
        Assert.Empty(MealRules.Violations(request, week));
    }

    [Fact]
    public void AScoreBelowZeroOnlyRanksLast()
    {
        var cards = Italian(12);
        var scores = cards.ToDictionary(card => card.Id, _ => 0m);
        scores[cards[0].Id] = -50m;
        var request = Request(recipes: cards, seed: 3) with { Appetite = scores };

        var week = MealPlanner.Plan(request);

        Assert.True(week.IsPlanned);
        Assert.DoesNotContain(Cooks(week), dinner => dinner.Recipe == cards[0].Id);
    }

    [Fact]
    public void NoRuleBreaksWhateverTheScores()
    {
        for (var seed = 0; seed < PlannerFingerprint.Seeds; seed++)
        {
            var random = new Random(seed + 9000);
            var request = PlannerFingerprint.RequestFor(seed);
            request = request with { Appetite = request.Recipes.ToDictionary(card => card.Id, _ => (decimal)random.Next(-20, 40)) };

            var week = MealPlanner.Plan(request);

            Assert.True(week.IsPlanned, "seed " + seed);
            Assert.Empty(MealRules.Violations(request, week));
            foreach (var dinner in Cooks(week))
            {
                var days = request.History.Where(meal => meal.Recipe == dinner.Recipe).Select(meal => meal.Day)
                    .Concat(Cooks(week).Where(other => other != dinner && other.Recipe == dinner.Recipe).Select(other => other.Day));
                Assert.All(days, day => Assert.True(Math.Abs(day.DayNumber - dinner.Day.DayNumber) >= MealPlanner.RepeatDays, "seed " + seed));
            }

            Assert.Equal(MealWeekCodec.Write(week), MealWeekCodec.Write(MealPlanner.Plan(request)));
        }
    }

    [Fact]
    public void TheProfileFeedsTheRequestItsScores()
    {
        var box = TasteData.Box();
        var profile = TasteData.Build([TasteData.Rate(TasteData.Sam, TasteData.Risotto, TasteVerdict.Loved), TasteData.Rate(TasteData.Lee, TasteData.Risotto, TasteVerdict.Loved)]);
        var inputs = TastePlannerInputs.From(profile, TasteData.People(), box);

        var applied = inputs.Apply(Request(recipes: box));

        Assert.Equal(inputs.Appetite, applied.Appetite);
        Assert.True(applied.Appetite[TasteData.Risotto] > applied.Appetite[TasteData.Omelette]);
    }

    [Fact]
    public void NoProfileLeavesTheRequestAsItWas()
    {
        var request = Request();

        var applied = TastePlannerInputs.From(TasteProfile.Empty, TasteData.People(), request.Recipes).Apply(request);

        Assert.Same(request, applied);
    }
}
