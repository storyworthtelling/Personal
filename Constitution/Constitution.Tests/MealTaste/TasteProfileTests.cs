using System.Reflection;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The invisible profile: built from ratings, lasting notes and seed dislikes, per person, and shown to nobody.</summary>
public class TasteProfileTests
{
    private static Recipe Card(RecipeId id) => Box().Single(recipe => recipe.Id == id);

    [Fact]
    public void SeedsFromDislikes()
    {
        var notes = TasteSeed.Notes(Lee, ["mushrooms", "Mexican"], Day(0));

        var profile = Build(notes: notes);

        var lee = profile.For(Lee)!;
        Assert.True(lee.Ingredients["mushroom"] < 0);
        Assert.True(lee.Cuisines[Cuisine.Mexican] < 0);
        Assert.True(lee.Appetite(Card(Risotto)) < lee.Appetite(Card(Pasta)));
        Assert.True(lee.Appetite(Card(Burritos)) < lee.Appetite(Card(Omelette)));
        Assert.Equal(0m, profile.For(Sam)!.Appetite(Card(Risotto)));
        Assert.Empty(lee.NeverAgain);
    }

    [Fact]
    public void ASeedDislikeIsNotANeverAgain()
    {
        var profile = Build(notes: TasteSeed.Notes(Lee, ["mushrooms"], Day(0)));

        Assert.Empty(profile.For(Lee)!.NeverAgain);
    }

    [Fact]
    public async Task AOneOffDoesNotStick()
    {
        var judge = new FakeTasteJudge(evidence => new TasteJudgement.OneOff());
        var evidence = new TasteEvidence(Robin, TasteEvidenceKind.Denial, "too tired for rice tonight", Risotto, Day(1));

        var learned = await TasteLearner.LearnAsync([evidence], judge, CancellationToken.None);
        var profile = Build(notes: learned.Notes);

        Assert.Empty(learned.Notes);
        Assert.Equal(TasteOutcome.OneOff, Assert.Single(learned.Outcomes));
        Assert.Empty(profile.For(Robin)!.Ingredients);
        Assert.Empty(profile.For(Robin)!.Cuisines);
        Assert.Empty(profile.For(Robin)!.NeverAgain);
    }

    [Fact]
    public async Task ALastingLessonUpdatesOnlyThatPerson()
    {
        var judge = new FakeTasteJudge(evidence => new TasteJudgement.Lasting([new TasteLesson(TasteSubjectKind.Ingredient, "mushrooms", TasteStance.Dislikes)]));
        var evidence = new TasteEvidence(Robin, TasteEvidenceKind.Denial, "I just don't like mushrooms", Risotto, Day(1));

        var learned = await TasteLearner.LearnAsync([evidence], judge, CancellationToken.None);
        var profile = Build(notes: learned.Notes);

        Assert.Equal(TasteOutcome.Lasting, Assert.Single(learned.Outcomes));
        Assert.True(profile.For(Robin)!.Ingredients["mushroom"] < 0);
        Assert.Empty(profile.For(Juniper)!.Ingredients);
        Assert.Empty(profile.For(Sam)!.Ingredients);
    }

    [Fact]
    public void ProfileIsNeverRendered()
    {
        var profile = Build([Rate(Sam, Risotto, TasteVerdict.NeverAgain, reason: "tasted of soap")], TasteSeed.Notes(Lee, ["olives"], Day(0)));

        foreach (var text in new[] { profile.ToString(), profile.For(Sam)!.ToString(), profile.For(Lee)!.ToString() })
        {
            Assert.DoesNotContain(Risotto.Value, text, StringComparison.Ordinal);
            Assert.DoesNotContain("olive", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("soap", text, StringComparison.OrdinalIgnoreCase);
        }

        // No member of the profile hands back words a page could print.
        foreach (var type in new[] { typeof(TasteProfile), typeof(PersonTaste), typeof(TastePlannerInputs) })
        {
            var wordy = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(member => member is PropertyInfo { PropertyType: var p } && p == typeof(string)
                    || member is MethodInfo { ReturnType: var r, IsSpecialName: false } && r == typeof(string))
                .Select(member => member.Name)
                .Where(name => name != nameof(ToString))
                .ToList();
            Assert.Empty(wordy);
        }
    }

    [Fact]
    public void NothingOutsideTheTasteFilesNamesTheProfile()
    {
        var root = ConstitutionSource.Root();
        var offenders = ConstitutionSource.Files(".cs")
            .Where(file => !file.StartsWith(Path.Combine(root, "Constitution.Tests"), StringComparison.Ordinal))
            .Where(file => !Path.GetFileName(file).StartsWith("Taste", StringComparison.Ordinal))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("TasteProfile", StringComparison.Ordinal)
                    || text.Contains("PersonTaste", StringComparison.Ordinal)
                    || text.Contains("TastePlannerInputs", StringComparison.Ordinal);
            })
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NeverAgainIsPerPerson()
    {
        var profile = Build([Rate(Sam, Risotto, TasteVerdict.NeverAgain), Rate(Lee, Risotto, TasteVerdict.Loved)]);

        Assert.Equal([Risotto], profile.For(Sam)!.NeverAgain);
        Assert.Empty(profile.For(Lee)!.NeverAgain);
        Assert.Equal([Risotto], profile.For(Lee)!.Loved);
    }

    [Fact]
    public void TheLatestRatingWins()
    {
        var lifted = Build([Rate(Sam, Risotto, TasteVerdict.NeverAgain, 0), Rate(Sam, Risotto, TasteVerdict.Loved, 14)]).For(Sam)!;
        var turned = Build([Rate(Sam, Pasta, TasteVerdict.Loved, 0), Rate(Sam, Pasta, TasteVerdict.NeverAgain, 14)]).For(Sam)!;

        Assert.Empty(lifted.NeverAgain);
        Assert.Equal([Risotto], lifted.Loved);
        Assert.Equal([Pasta], turned.NeverAgain);
        Assert.Empty(turned.Loved);
    }

    [Fact]
    public void TheSameRatingTwiceCountsOnce()
    {
        var once = Build([Rate(Sam, Pasta, TasteVerdict.Loved), Rate(Sam, Risotto, TasteVerdict.Loved), Rate(Sam, Curry, TasteVerdict.Fine)]);
        var twice = Build(
        [
            Rate(Sam, Pasta, TasteVerdict.Loved), Rate(Sam, Pasta, TasteVerdict.Loved),
            Rate(Sam, Risotto, TasteVerdict.Loved), Rate(Sam, Curry, TasteVerdict.Fine), Rate(Sam, Curry, TasteVerdict.Fine),
        ]);

        Assert.Equal(TasteProfileCodec.Write(once), TasteProfileCodec.Write(twice));
        Assert.Equal(once.For(Sam)!.Cuisines[Cuisine.Italian], twice.For(Sam)!.Cuisines[Cuisine.Italian]);
    }

    [Fact]
    public void ReratingTheSameDayTheLastWriteWins()
    {
        var profile = Build([Rate(Sam, Pasta, TasteVerdict.NeverAgain, 2), Rate(Sam, Pasta, TasteVerdict.Loved, 2)]);

        Assert.Equal([Pasta], profile.For(Sam)!.Loved);
        Assert.Empty(profile.For(Sam)!.NeverAgain);
    }

    [Fact]
    public void TheOrderTheyArriveInChangesNothing()
    {
        var ratings = new[]
        {
            Rate(Sam, Pasta, TasteVerdict.Loved, 0), Rate(Lee, Curry, TasteVerdict.NeverAgain, 1), Rate(Robin, Burritos, TasteVerdict.Fine, 2),
            Rate(Sam, Risotto, TasteVerdict.Fine, 3), Rate(Juniper, Tacos, TasteVerdict.Loved, 4),
        };

        Assert.Equal(TasteProfileCodec.Write(Build(ratings)), TasteProfileCodec.Write(Build(ratings.Reverse())));
    }

    [Fact]
    public void SilenceInventsNoRating()
    {
        var profile = Build();

        Assert.All(profile.People, person =>
        {
            Assert.Empty(person.Loved);
            Assert.Empty(person.Fine);
            Assert.Empty(person.NeverAgain);
            Assert.Empty(person.Cuisines);
            Assert.Empty(person.Ingredients);
            Assert.Empty(person.Dishes);
        });
        Assert.Empty(profile.KidFriendly);
    }

    [Fact]
    public void ARatingFromSomeoneWhoLeftIsIgnored()
    {
        var gone = new PersonId("grandpa");

        var profile = Build([new TasteRating(gone, Pasta, Day(0), TasteVerdict.NeverAgain)]);

        Assert.Null(profile.For(gone));
        Assert.Equal(4, profile.People.Count);
    }

    [Fact]
    public void ANeverAgainOnACardNoLongerInTheBoxStillStands()
    {
        var retired = new RecipeId("retired-stew");

        var profile = Build([new TasteRating(Sam, retired, Day(0), TasteVerdict.NeverAgain)]);

        Assert.Equal([retired], profile.For(Sam)!.NeverAgain);
        Assert.Empty(profile.For(Sam)!.Cuisines);
    }

    [Fact]
    public void RatingsShapeACuisineAffinity()
    {
        var sam = Build([
            Rate(Sam, Pasta, TasteVerdict.Loved), Rate(Sam, Risotto, TasteVerdict.Fine),
            Rate(Sam, Tacos, TasteVerdict.NeverAgain), Rate(Sam, Curry, TasteVerdict.Fine),
        ]).For(Sam)!;

        Assert.Equal(TasteWeights.Loved + TasteWeights.Fine, sam.Cuisines[Cuisine.Italian]);
        Assert.Equal(TasteWeights.NeverAgain, sam.Cuisines[Cuisine.Mexican]);
        Assert.Equal(TasteWeights.Fine, sam.Cuisines[Cuisine.Indian]);
        Assert.False(sam.Cuisines.ContainsKey(Cuisine.French));
    }

    [Fact]
    public void AnIngredientLovedAcrossTwoDishesIsAnAffinityButSaltIsNot()
    {
        var sam = Build([
            Rate(Sam, Risotto, TasteVerdict.Loved), Rate(Sam, Pasta, TasteVerdict.Loved),
            Rate(Sam, Omelette, TasteVerdict.Fine), Rate(Sam, Chicken, TasteVerdict.NeverAgain),
        ]).For(Sam)!;

        Assert.True(sam.Ingredients["onion"] > 0);
        Assert.False(sam.Ingredients.ContainsKey("salt"));
        Assert.False(sam.Ingredients.ContainsKey("mushroom"));
        Assert.False(sam.Ingredients.ContainsKey("chicken"));
    }

    [Fact]
    public void AnIngredientNeedsTwoDishesBeforeItCounts()
    {
        var sam = Build([Rate(Sam, Risotto, TasteVerdict.Loved), Rate(Sam, Chicken, TasteVerdict.NeverAgain)]).For(Sam)!;

        Assert.Empty(sam.Ingredients);
    }

    [Fact]
    public void PluralsAndCapitalsMatch()
    {
        var sam = Build(notes: [Note(Sam, TasteSubjectKind.Ingredient, "TOMATO", TasteStance.Dislikes)]).For(Sam)!;

        Assert.True(sam.Appetite(Card(Pasta)) < 0);
    }

    [Fact]
    public void AMultiWordNoteMatchesThePhraseNotEitherWord()
    {
        var sam = Build(notes: [Note(Sam, TasteSubjectKind.Ingredient, "black beans", TasteStance.Dislikes)]).For(Sam)!;

        Assert.True(sam.Appetite(Card(Burritos)) < 0);
        Assert.Equal(0m, sam.Appetite(Card(Curry)));
    }

    [Fact]
    public void ALikedCuisineNoteRaisesItsDishes()
    {
        var sam = Build(notes: [Note(Sam, TasteSubjectKind.Cuisine, "french", TasteStance.Likes)]).For(Sam)!;

        Assert.Equal(TasteWeights.Note, sam.Cuisines[Cuisine.French]);
        Assert.True(sam.Appetite(Card(Tart)) > sam.Appetite(Card(Chicken)));
    }

    [Fact]
    public void ADishNoteMovesThatDishAlone()
    {
        var sam = Build(notes: [Note(Sam, TasteSubjectKind.Dish, "corn-tacos", TasteStance.Dislikes)]).For(Sam)!;

        Assert.True(sam.Appetite(Card(Tacos)) < sam.Appetite(Card(Burritos)));
        Assert.Empty(sam.NeverAgain);
    }

    [Fact]
    public void ARatedDishIsJudgedByItsRatingNotItsParts()
    {
        var sam = Build([Rate(Sam, Risotto, TasteVerdict.Loved)], TasteSeed.Notes(Sam, ["mushrooms"], Day(0))).For(Sam)!;

        Assert.Equal(TasteWeights.Loved, sam.Appetite(Card(Risotto)));
    }

    [Fact]
    public void AppetiteOrdersLovedAboveUnratedAboveNeverAgain()
    {
        var sam = Build([Rate(Sam, Curry, TasteVerdict.Loved), Rate(Sam, Tart, TasteVerdict.NeverAgain)]).For(Sam)!;

        Assert.True(sam.Appetite(Card(Curry)) > sam.Appetite(Card(Omelette)));
        Assert.True(sam.Appetite(Card(Omelette)) > sam.Appetite(Card(Tart)));
    }

    [Fact]
    public void KidFriendlyMeansAChildTookItAndNoChildTurnedItDown()
    {
        var profile = Build([
            Rate(Robin, Burritos, TasteVerdict.Loved), Rate(Robin, Tacos, TasteVerdict.Fine), Rate(Robin, Pasta, TasteVerdict.NeverAgain),
            Rate(Juniper, Tacos, TasteVerdict.NeverAgain), Rate(Juniper, Chicken, TasteVerdict.Fine),
            Rate(Sam, Curry, TasteVerdict.Loved),
        ]);

        Assert.Equal([Burritos, Chicken], profile.KidFriendly);
        Assert.DoesNotContain(Curry, profile.KidFriendly);
    }

    [Fact]
    public void AGrownUpsRatingNeverMakesADishKidFriendly()
    {
        var profile = Build([Rate(Sam, Curry, TasteVerdict.Loved)]);

        Assert.Empty(profile.KidFriendly);
        Assert.False(profile.For(Sam)!.IsChild);
        Assert.True(profile.For(Robin)!.IsChild);
    }

    [Fact]
    public void WrittenReasonsAreNotKeptInTheProfile()
    {
        var json = TasteProfileCodec.Write(Build([Rate(Sam, Risotto, TasteVerdict.NeverAgain, reason: "tasted of soap")]));

        Assert.DoesNotContain("soap", json, StringComparison.Ordinal);
    }
}
