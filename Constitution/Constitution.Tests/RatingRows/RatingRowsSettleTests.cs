using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using Constitution.Tests.HouseholdFile;
using Constitution.Tests.MealReadBack;
using static Constitution.Tests.MealReadBack.MealRig;

namespace Constitution.Tests.RatingRows;

/// <summary>The hook rates the person and card the page was built for, by id, and only looks them up by words for a document that carries none.</summary>
public class RatingRowsSettleTests
{
    private static readonly PersonId Robin = new("robin");
    private static readonly PersonId Juniper = new("juniper");
    private static readonly PersonId Sam = new("sam");
    private static readonly RecipeId Noodles = new("buttered-noodles");

    private static InkResolution Circle(int row, string word) => new InkResolution.Circled(RecipeIds.RatingGroup(row), word);

    private static ExpectedDocument WithoutRows(ExpectedDocument document) =>
        new(document.Kind, document.Day, document.Subject, document.Person, document.Session, document.Pages);

    private static void Rename(MealRig rig, PersonId id, string name) =>
        rig.Edit(held => HouseholdRecords.Put(held, RosterMaps.Person, new RosterPerson(id, name, MemberRole.Child, null, false, null), RosterData.At(6000), RosterData.OwnerWriter));

    private static void Retitle(MealRig rig, string title) =>
        rig.Edit(held => HouseholdRecords.Put(held, RecipeMaps.Recipe, Noodles() with { Title = title }, RosterData.At(6001), RosterData.OwnerWriter));

    [Fact]
    public void ARenamedPersonIsStillRatedByTheirId()
    {
        using var rig = new MealRig();
        Rename(rig, Robin, "Robbie");
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(3, "Never again")));

        var rating = Assert.Single(rig.Ratings());
        Assert.Equal(new TasteRating(Robin, Noodles, DinnerDay, TasteVerdict.NeverAgain), rating);
    }

    [Fact]
    public void AKidsDishWithAnotherCardTitleIsRatedByItsCard()
    {
        using var rig = new MealRig();
        Retitle(rig, "Noodles with butter, the kids' way");
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(3, "Fine"), Circle(4, "Loved")));

        Assert.Equal([Juniper, Robin], rig.Ratings().Select(rating => rating.Person).OrderBy(person => person.Value, StringComparer.Ordinal).ToList());
        Assert.All(rig.Ratings(), rating => Assert.Equal(Noodles, rating.Recipe));
    }

    [Fact]
    public void TwoCardsOfOneTitleAreNoLongerAnAmbiguity()
    {
        using var rig = new MealRig();
        var twin = Noodles() with { Id = new RecipeId("buttered-noodles-two") };
        rig.Edit(held => HouseholdRecords.Put(held, RecipeMaps.Recipe, twin, RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved")));

        Assert.Equal(
            [new TasteRating(Robin, Noodles, DinnerDay, TasteVerdict.Loved), new TasteRating(Sam, new RecipeId("sheet-pan-chicken"), DinnerDay, TasteVerdict.Fine)],
            rig.Ratings().OrderBy(rating => rating.Person.Value, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void TwoPeopleOfOneNameAreNoLongerAnAmbiguity()
    {
        using var rig = new MealRig();
        rig.Edit(held => HouseholdRecords.Put(held, RosterMaps.Person, new RosterPerson(new PersonId("robin-two"), "Robin", MemberRole.Adult, null, false, null), RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(3, "Fine")));

        Assert.Equal(Robin, Assert.Single(rig.Ratings()).Person);
    }

    [Fact]
    public void ACarriedPersonRemovedFromTheRosterIsStillReported()
    {
        using var rig = new MealRig();
        rig.Edit(held => HouseholdRecords.Remove(held, RosterMaps.Person.KeyOf(new RosterPerson(Robin, "Robin", MemberRole.Child, null, false, null)), RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void ACarriedCardRemovedFromTheBoxIsStillReported()
    {
        using var rig = new MealRig();
        rig.Edit(held => HouseholdRecords.Remove(held, RecipeMaps.Recipe, Noodles(), RosterData.At(6000), RosterData.OwnerWriter));
        var document = Recipe();

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void ADocumentWithoutRowsStillFindsThemByWords()
    {
        using var rig = new MealRig();
        var document = WithoutRows(Recipe());

        rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved")));

        Assert.Equal([Robin, Sam], rig.Ratings().Select(rating => rating.Person).OrderBy(person => person.Value, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void WithoutRowsARenamedPersonFailsLoudly()
    {
        using var rig = new MealRig();
        Rename(rig, Robin, "Robbie");
        var document = WithoutRows(Recipe());

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(document, RecipeResult(document, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }

    [Fact]
    public void ARowTheDocumentDoesNotCarryFallsBackAlone()
    {
        using var rig = new MealRig();
        Rename(rig, Robin, "Robbie");
        var full = Recipe();
        var partial = new ExpectedDocument(full.Kind, full.Day, full.Subject, full.Person, full.Session, full.Pages, full.RatingRows.Where(row => row.Group != "rate-3").ToList());

        Assert.ThrowsAny<InvalidOperationException>(
            () => rig.Hook.Settled(partial, RecipeResult(partial, NextMorning, Circle(1, "Fine"), Circle(3, "Loved"))));

        Assert.Equal([Sam], rig.Ratings().Select(rating => rating.Person).ToList());
    }
}
