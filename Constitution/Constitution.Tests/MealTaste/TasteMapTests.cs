using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The typed maps for a <c>rating</c> and a <c>taste-note</c> record: frozen names, frozen words, one record per person, card and day.</summary>
public class TasteMapTests
{
    private static HouseholdDocument PutRating(TasteRating rating, HouseholdDocument? into = null, long at = 1) =>
        HouseholdRecords.Put(into ?? HouseholdDocument.Empty, TasteMaps.Rating, rating, RecipeData.At(at), RecipeData.OwnerWriter);

    private static HouseholdDocument PutNote(TasteNote note, HouseholdDocument? into = null, long at = 1) =>
        HouseholdRecords.Put(into ?? HouseholdDocument.Empty, TasteMaps.Note, note, RecipeData.At(at), RecipeData.OwnerWriter);

    [Fact]
    public void TheKindsAreTheFrozenOnes()
    {
        Assert.Equal("rating", TasteMaps.Rating.Kind);
        Assert.Equal("taste-note", TasteMaps.Note.Kind);
    }

    [Fact]
    public void ARatingKeyIsPersonCardAndDay()
    {
        Assert.Equal("rating/sam:tomato-pasta:2026-10-21", TasteMaps.Rating.KeyOf(Rate(Sam, Pasta, TasteVerdict.Loved, 2)).Value);
    }

    [Fact]
    public void TheRatingFieldNamesAreFrozen()
    {
        var names = TasteMaps.Rating.FieldsOf(Rate(Sam, Pasta, TasteVerdict.Loved)).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["day", "person", "reason", "recipe", "verdict"], names);
    }

    [Fact]
    public void TheRatingValuesAreFrozen()
    {
        var fields = TasteMaps.Rating.FieldsOf(Rate(Robin, Tacos, TasteVerdict.NeverAgain, 3, "too crunchy"));

        Assert.Equal("robin", fields["person"]);
        Assert.Equal("corn-tacos", fields["recipe"]);
        Assert.Equal("2026-10-22", fields["day"]);
        Assert.Equal("never-again", fields["verdict"]);
        Assert.Equal("too crunchy", fields["reason"]);
    }

    [Theory]
    [InlineData(TasteVerdict.Loved, null)]
    [InlineData(TasteVerdict.Fine, "")]
    [InlineData(TasteVerdict.NeverAgain, "it was grey and cold")]
    public void ARatingRoundTrips(TasteVerdict verdict, string? reason)
    {
        var rating = Rate(Lee, Curry, verdict, 1, reason);

        var set = HouseholdRecords.Read(PutRating(rating), TasteMaps.Rating);

        Assert.Empty(set.Unreadable);
        var back = Assert.Single(set.Items);
        Assert.Equal(rating.Person, back.Person);
        Assert.Equal(rating.Recipe, back.Recipe);
        Assert.Equal(rating.Day, back.Day);
        Assert.Equal(rating.Verdict, back.Verdict);
        Assert.Equal(rating.Reason, back.Reason);
    }

    [Fact]
    public void RatingTheSameDishTheSameDayTwiceIsOneRecord()
    {
        var document = PutRating(Rate(Sam, Pasta, TasteVerdict.Fine), at: 1);
        document = PutRating(Rate(Sam, Pasta, TasteVerdict.Loved), document, at: 2);

        var back = Assert.Single(HouseholdRecords.Read(document, TasteMaps.Rating).Items);
        Assert.Equal(TasteVerdict.Loved, back.Verdict);
    }

    [Fact]
    public void ADifferentDayOrPersonIsAnotherRecord()
    {
        var document = PutRating(Rate(Sam, Pasta, TasteVerdict.Fine, 0), at: 1);
        document = PutRating(Rate(Sam, Pasta, TasteVerdict.Fine, 7), document, at: 2);
        document = PutRating(Rate(Lee, Pasta, TasteVerdict.Fine, 0), document, at: 3);

        Assert.Equal(3, HouseholdRecords.Read(document, TasteMaps.Rating).Items.Count);
    }

    [Fact]
    public void AnUnknownVerdictWordIsUnreadableAndNamesTheField()
    {
        var fields = new Dictionary<string, string>(TasteMaps.Rating.FieldsOf(Rate(Sam, Pasta, TasteVerdict.Loved))) { ["verdict"] = "adored" };
        var odd = HouseholdRecord.Write(TasteMaps.Rating.KeyOf(Rate(Sam, Pasta, TasteVerdict.Loved)), 9, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), TasteMaps.Rating);

        Assert.Empty(set.Items);
        Assert.Contains("verdict", Assert.Single(set.Unreadable).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("person", "Not A Person")]
    [InlineData("recipe", "")]
    [InlineData("day", "last tuesday")]
    public void ABadFieldMakesTheRatingUnreadable(string field, string value)
    {
        var fields = new Dictionary<string, string>(TasteMaps.Rating.FieldsOf(Rate(Sam, Pasta, TasteVerdict.Loved))) { [field] = value };
        var odd = HouseholdRecord.Write(TasteMaps.Rating.KeyOf(Rate(Sam, Pasta, TasteVerdict.Loved)), 1, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), TasteMaps.Rating);

        Assert.Single(set.Unreadable);
    }

    [Fact]
    public void ANewerWritersFieldSurvivesAnEdit()
    {
        var newer = HouseholdRecord.Write(
            TasteMaps.Rating.KeyOf(Rate(Sam, Pasta, TasteVerdict.Fine)), 7,
            new Dictionary<string, string>(TasteMaps.Rating.FieldsOf(Rate(Sam, Pasta, TasteVerdict.Fine))) { ["seasoning"] = "extra" },
            RecipeData.At(1), RecipeData.PartnerWriter);

        var edited = PutRating(Rate(Sam, Pasta, TasteVerdict.Loved), HouseholdDocument.Empty.With(newer), at: 2);

        Assert.Equal("extra", edited.Find(TasteMaps.Rating.KeyOf(Rate(Sam, Pasta, TasteVerdict.Fine)))!.Fields["seasoning"]);
    }

    [Fact]
    public void ANoteKeyIsPersonKindAndSubject()
    {
        var note = Note(Lee, TasteSubjectKind.Ingredient, "Green Olives", TasteStance.Dislikes);

        Assert.Equal("taste-note/lee:ingredient:green%20olives", TasteMaps.Note.KeyOf(note).Value);
    }

    [Fact]
    public void TheNoteFieldNamesAndValuesAreFrozen()
    {
        var fields = TasteMaps.Note.FieldsOf(Note(Lee, TasteSubjectKind.Cuisine, "mexican", TasteStance.Dislikes, TasteOrigin.Seeded));

        Assert.Equal(["kind", "learned", "origin", "person", "stance", "subject"], fields.Keys.Order(StringComparer.Ordinal).ToList());
        Assert.Equal("lee", fields["person"]);
        Assert.Equal("cuisine", fields["kind"]);
        Assert.Equal("mexican", fields["subject"]);
        Assert.Equal("dislikes", fields["stance"]);
        Assert.Equal("seeded", fields["origin"]);
        Assert.Equal("2026-10-19", fields["learned"]);
    }

    [Theory]
    [InlineData(TasteSubjectKind.Cuisine, "italian", TasteStance.Likes, TasteOrigin.Learned)]
    [InlineData(TasteSubjectKind.Ingredient, "green olives", TasteStance.Dislikes, TasteOrigin.Seeded)]
    [InlineData(TasteSubjectKind.Dish, "corn-tacos", TasteStance.Dislikes, TasteOrigin.Learned)]
    public void ANoteRoundTrips(TasteSubjectKind kind, string subject, TasteStance stance, TasteOrigin origin)
    {
        var note = Note(Robin, kind, subject, stance, origin);

        var set = HouseholdRecords.Read(PutNote(note), TasteMaps.Note);

        Assert.Empty(set.Unreadable);
        Assert.Equal(note, Assert.Single(set.Items));
    }

    [Fact]
    public void ChangingYourMindAboutASubjectEditsTheOneRecord()
    {
        var document = PutNote(Note(Lee, TasteSubjectKind.Ingredient, "olives", TasteStance.Dislikes), at: 1);
        document = PutNote(Note(Lee, TasteSubjectKind.Ingredient, "Olives", TasteStance.Likes), document, at: 2);

        var back = Assert.Single(HouseholdRecords.Read(document, TasteMaps.Note).Items);
        Assert.Equal(TasteStance.Likes, back.Stance);
    }

    [Fact]
    public void AnUnknownStanceWordIsUnreadable()
    {
        var note = Note(Lee, TasteSubjectKind.Ingredient, "olives", TasteStance.Dislikes);
        var fields = new Dictionary<string, string>(TasteMaps.Note.FieldsOf(note)) { ["stance"] = "adores" };
        var odd = HouseholdRecord.Write(TasteMaps.Note.KeyOf(note), 9, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), TasteMaps.Note);

        Assert.Contains("stance", Assert.Single(set.Unreadable).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACuisineNoteWithAnUnknownCuisineIsUnreadableNotACrash()
    {
        var note = Note(Lee, TasteSubjectKind.Cuisine, "french", TasteStance.Likes);
        var fields = new Dictionary<string, string>(TasteMaps.Note.FieldsOf(note)) { ["subject"] = "martian" };
        var odd = HouseholdRecord.Write(TasteMaps.Note.KeyOf(note), 1, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        Assert.Single(HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), TasteMaps.Note).Unreadable);
    }
}
