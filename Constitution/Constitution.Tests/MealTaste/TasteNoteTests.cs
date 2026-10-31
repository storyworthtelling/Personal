using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The lasting lesson (kind taste-note) and the seed dislikes that become the first ones.</summary>
public class TasteNoteTests
{
    [Fact]
    public void ASubjectIsKeptInLowerCaseWithSingleSpaces()
    {
        Assert.Equal("green olives", Note(Sam, TasteSubjectKind.Ingredient, "  Green   OLIVES ", TasteStance.Dislikes).Subject);
    }

    [Fact]
    public void ACuisineSubjectMustBeAKnownCuisineWord()
    {
        Assert.Equal("mexican", Note(Sam, TasteSubjectKind.Cuisine, "Mexican", TasteStance.Dislikes).Subject);
        Assert.Throws<ArgumentException>(() => Note(Sam, TasteSubjectKind.Cuisine, "martian", TasteStance.Dislikes));
    }

    [Fact]
    public void ADishSubjectMustBeACardId()
    {
        Assert.Equal("corn-tacos", Note(Sam, TasteSubjectKind.Dish, "corn-tacos", TasteStance.Likes).Subject);
        Assert.Throws<ArgumentException>(() => Note(Sam, TasteSubjectKind.Dish, "Corn Tacos!", TasteStance.Likes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSubjectIsRefused(string subject) =>
        Assert.Throws<ArgumentException>(() => Note(Sam, TasteSubjectKind.Ingredient, subject, TasteStance.Dislikes));

    [Fact]
    public void AnIngredientHasALength()
    {
        Assert.Throws<ArgumentException>(() => Note(Sam, TasteSubjectKind.Ingredient, new string('a', TasteNote.MaxSubjectLength + 1), TasteStance.Dislikes));
    }

    [Fact]
    public void TryCreateAnswersNullInsteadOfThrowing()
    {
        Assert.Null(TasteNote.TryCreate(Sam, TasteSubjectKind.Cuisine, "martian", TasteStance.Dislikes, TasteOrigin.Learned, Day(0)));
        Assert.NotNull(TasteNote.TryCreate(Sam, TasteSubjectKind.Cuisine, "french", TasteStance.Dislikes, TasteOrigin.Learned, Day(0)));
    }

    [Fact]
    public void ToStringNamesNoSubject()
    {
        var text = Note(Sam, TasteSubjectKind.Ingredient, "mushrooms", TasteStance.Dislikes).ToString();

        Assert.DoesNotContain("mushroom", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASeedWordThatIsACuisineIsACuisineNote()
    {
        var notes = TasteSeed.Notes(Lee, ["Mexican"], Day(0));

        var note = Assert.Single(notes);
        Assert.Equal(TasteSubjectKind.Cuisine, note.Kind);
        Assert.Equal("mexican", note.Subject);
    }

    [Fact]
    public void AnyOtherSeedWordIsAnIngredientNote()
    {
        var note = Assert.Single(TasteSeed.Notes(Lee, ["Mushrooms"], Day(0)));

        Assert.Equal(TasteSubjectKind.Ingredient, note.Kind);
        Assert.Equal("mushrooms", note.Subject);
        Assert.Equal(TasteStance.Dislikes, note.Stance);
        Assert.Equal(TasteOrigin.Seeded, note.Origin);
        Assert.Equal(Lee, note.Person);
        Assert.Equal(Day(0), note.Learned);
    }

    [Fact]
    public void BlanksAndRepeatsAreLeftOutOfTheSeed()
    {
        var notes = TasteSeed.Notes(Lee, ["olives", " ", "", null, "Olives", "  olives "], Day(0));

        Assert.Single(notes);
    }

    [Fact]
    public void ASeedWordTooLongToBeAnIngredientIsLeftOutNotThrown()
    {
        var notes = TasteSeed.Notes(Lee, [new string('a', TasteNote.MaxSubjectLength + 1), "olives"], Day(0));

        Assert.Equal("olives", Assert.Single(notes).Subject);
    }
}
