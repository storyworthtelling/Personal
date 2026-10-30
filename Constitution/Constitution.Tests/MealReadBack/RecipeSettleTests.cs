using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;
using static Constitution.Tests.MealReadBack.MealRig;

namespace Constitution.Tests.MealReadBack;

/// <summary>What a settled recipe page leaves in the household file: each circled lane is that person's rating of their own dish, and nothing else.</summary>
public class RecipeSettleTests
{
    private static readonly PersonId Sam = new("sam");
    private static readonly PersonId Robin = new("robin");
    private static readonly PersonId Juniper = new("juniper");
    private static readonly RecipeId Chicken = new("sheet-pan-chicken");
    private static readonly RecipeId Noodles = new("buttered-noodles");

    private static InkResolution Circle(int row, string word) => new InkResolution.Circled(RecipeIds.RatingGroup(row), word);

    [Fact]
    public void EachCircleIsThatPersonsRatingOfTheirDish()
    {
        using var rig = new MealRig();
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Never again"), Circle(4, "Loved")));

        Assert.Equal(
            [
                new TasteRating(Juniper, Noodles, DinnerDay, TasteVerdict.Loved),
                new TasteRating(Robin, Noodles, DinnerDay, TasteVerdict.NeverAgain),
                new TasteRating(Sam, Chicken, DinnerDay, TasteVerdict.Fine),
            ],
            rig.Ratings().OrderBy(rating => rating.Person.Value, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void NoCircleWritesNothing()
    {
        using var rig = new MealRig();
        var before = rig.Document();
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning));

        Assert.True(before.ContentEquals(rig.Document()));
    }

    [Fact]
    public void WeDidNotCookIgnoresEveryRating()
    {
        using var rig = new MealRig();
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Loved"), Circle(2, "Fine"), new InkResolution.Ticked(RecipeIds.DidntCook)));

        Assert.Empty(rig.Ratings());
    }

    [Fact]
    public void ADoubtfulRowWritesNothingForThatRowAlone()
    {
        using var rig = new MealRig();
        var document = Recipe();
        var doubt = new InkResolution.NeedsAsking(RecipeIds.RatingGroup(3), "Robin's rating of Buttered noodles", "Fine", "Never again", "cause-rate-3");

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Loved"), doubt, Circle(4, "Fine")));

        Assert.Equal([Juniper, Sam], rig.Ratings().Select(rating => rating.Person).OrderBy(person => person.Value, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void ARowNotReadAndAWordThatIsNoLaneWriteNothing()
    {
        using var rig = new MealRig();
        var document = Recipe();

        rig.Hook.Settled(
            document,
            RecipeResult(document, NextMorning, new InkResolution.NotRead(RecipeIds.RatingGroup(1), "The page was not read."), Circle(2, "Spicy")));

        Assert.Empty(rig.Ratings());
    }

    [Fact]
    public void TheSameRatingTwiceIsOneRecordLeftAlone()
    {
        using var rig = new MealRig();
        var document = Recipe();
        var result = RecipeResult(document, NextMorning, Circle(1, "Fine"));
        rig.Hook.Settled(document, result);
        var once = rig.Document();

        rig.Hook.Settled(document, result with { ReadAt = NextMorning.AddDays(1) });

        Assert.True(once.ContentEquals(rig.Document()));
        Assert.Single(rig.Ratings());
    }

    [Fact]
    public void ADifferentCircleTheSameDayIsTheSameRecordEdited()
    {
        using var rig = new MealRig();
        var document = Recipe();
        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine")));

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Loved")));

        Assert.Equal(TasteVerdict.Loved, Assert.Single(rig.Ratings()).Verdict);
    }

    [Fact]
    public void AnEaterNotOnTheRosterIsReportedAfterTheOthersAreKept()
    {
        using var rig = new MealRig();
        rig.Edit(document => HouseholdRecords.Remove(document, RosterMaps.Person.KeyOf(new RosterPerson(Robin, "Robin", MemberRole.Child, null, false, null)), RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void ADishNotInTheBoxIsReportedAfterTheOthersAreKept()
    {
        using var rig = new MealRig();
        rig.Edit(document => HouseholdRecords.Remove(document, RecipeMaps.Recipe, Noodles(), RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void TwoCardsOfOneTitleAreNotGuessedBetween()
    {
        using var rig = new MealRig();
        var twin = Noodles() with { Id = new RecipeId("buttered-noodles-two") };
        rig.Edit(document => HouseholdRecords.Put(document, RecipeMaps.Recipe, twin, RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void AnUnreadableFileIsNeverWrittenOver()
    {
        using var rig = new MealRig();
        rig.MakeUnreadable();
        var before = rig.Document();
        var document = Recipe();

        Assert.Throws<HouseholdUnreadableException>(() => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"))));

        Assert.True(before.ContentEquals(rig.Document()));
    }

    [Fact]
    public void ARatingThisBuildCannotReadIsLeftAndReported()
    {
        using var rig = new MealRig();
        var existing = new TasteRating(Sam, Chicken, DinnerDay, TasteVerdict.Loved);
        var garbled = TasteMaps.Rating.FieldsOf(existing).ToDictionary(pair => pair.Key, pair => pair.Value);
        garbled["verdict"] = "a-word-from-a-newer-build";
        rig.Edit(held => held.With(HouseholdRecord.Write(TasteMaps.Rating.KeyOf(existing), RecordSchema.Version, garbled, RosterData.At(5000), RosterData.PartnerWriter)));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal(garbled, rig.Document().Live(TasteMaps.Rating.KeyOf(existing))!.Fields);
        Assert.Contains(rig.Ratings(), rating => rating.Person == Robin);
    }

    [Fact]
    public void OnlyRatingRecordsAreWritten()
    {
        using var rig = new MealRig();
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(2, "Never again")));

        Assert.Equal(["person", "rating", "recipe"], rig.Kinds());
    }

    [Fact]
    public void ObservingARecipeWritesNothing()
    {
        using var rig = new MealRig();
        var before = rig.Document();
        var document = Recipe();

        rig.Hook.Observed(document, RecipeResult(document, NextMorning, Circle(1, "Fine")));

        Assert.True(before.ContentEquals(rig.Document()));
    }
}
