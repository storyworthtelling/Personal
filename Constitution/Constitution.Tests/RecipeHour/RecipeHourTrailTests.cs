using System.Text.RegularExpressions;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Tests.RecipeHour;

/// <summary>The recipe hour's words in the Body trail: fixed phrases, each made of vocabulary, none carrying a title, a name or a number.</summary>
public class RecipeHourTrailTests
{
    [Fact]
    public void EachKindOfOutcomeHasItsOwnPhrase()
    {
        Assert.Equal("meal recipes: none due", MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Nothing("None.")));
        Assert.Equal("meal recipes not delivered, not configured", MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.NotConfigured, "x")));
        Assert.Equal("meal recipes not delivered, weeks unreadable", MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.WeeksUnreadable, "x")));
        Assert.Equal("meal recipes not delivered, recipe box unreadable", MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.RecipesUnavailable, "x")));
        Assert.Equal("meal recipes not delivered, taste profile unreadable", MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.TasteUnreadable, "x")));
    }

    [Fact]
    public void ADoneOutcomeAddsNoLineBecauseTheStepAlreadyWroteItsOwn()
    {
        Assert.Equal(string.Empty, MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Done([])));
    }

    [Fact]
    public void EveryStopHasAPhrase()
    {
        foreach (var because in Enum.GetValues<MealRecipesStoppedBecause>())
        {
            Assert.False(string.IsNullOrWhiteSpace(MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(because, "x"))), because.ToString());
        }
    }

    [Fact]
    public void ThePassLinesForTheRecipePartNameIt()
    {
        Assert.Equal("meal recipes busy, try again later", MealPassTrail.LogPhrase(MealPassPart.Recipes, MealPassProblem.Busy, string.Empty));
        Assert.Equal("meal recipes failed: IOException", MealPassTrail.LogPhrase(MealPassPart.Recipes, MealPassProblem.Failed, "IOException"));
    }

    [Fact]
    public void EveryPhraseIsMadeOfVocabularyAndCarriesNoDigit()
    {
        var phrases = Enum.GetValues<MealRecipesStoppedBecause>()
            .Select(because => MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(because, "x")))
            .Append(MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Nothing("None.")))
            .Append(MealPassTrail.LogPhrase(MealPassPart.Recipes, MealPassProblem.Busy, string.Empty))
            .ToList();

        foreach (var phrase in phrases)
        {
            Assert.DoesNotMatch("[0-9A-Z]", phrase);
            foreach (var word in Regex.Matches(phrase, "[a-z]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Select(match => match.Value))
            {
                Assert.Contains(word, TrailLines.Vocabulary);
            }
        }
    }
}
