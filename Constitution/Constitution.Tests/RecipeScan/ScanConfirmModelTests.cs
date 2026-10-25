using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>The draft a scan becomes (C-6.5.3, book p.14): the words as read, editable only where p.14 lets them be, and validated by the committed recipe rules.</summary>
public class ScanConfirmModelTests
{
    private static readonly IReadOnlySet<RecipeId> NoneYet = new HashSet<RecipeId>();

    [Fact]
    public void TheDraftCarriesTheWordsAsRead()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        Assert.Equal("Apple crisp", model.Title);
        Assert.Equal(4, model.Ingredients.Count);
        Assert.Equal(2, model.Steps.Count);
        Assert.Equal(new ScanIngredientDraft("¾", "cup", "brown sugar"), model.Ingredients[1]);
        Assert.Equal(new ScanStepDraft("Bake 40 min, until bubbling", 40), model.Steps[1]);
        Assert.Null(model.Steps[0].Minutes);
    }

    [Fact]
    public void ThePageSaysWhatIsKept()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        Assert.Equal("Just scanned", model.Label);
        Assert.Equal("The picture isn’t kept, only the words.", model.StatusSentence);
        Assert.Equal("Looks right", model.KeepAction);
        Assert.Equal("Scan again", model.ScanAgainAction);
    }

    [Fact]
    public void TheCuisineStartsAsOtherAndCanBeMoved()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        Assert.Equal(Cuisine.Other, model.Cuisine);

        model.ChooseCuisine(Cuisine.American);

        Assert.Equal(Cuisine.American, model.Cuisine);
    }

    [Fact]
    public void ServingsStartUnknownAndMustBeSensible()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        Assert.Null(model.Servings);
        Assert.Null(model.Problem);

        model.SetServings(6);
        Assert.Null(model.Problem);

        model.SetServings(0);
        Assert.NotNull(model.Problem);

        model.SetServings(Recipe.MaxServings + 1);
        Assert.NotNull(model.Problem);
    }

    [Fact]
    public void ACardWithNoTitleAsksForOne()
    {
        var model = ScanConfirmModel.From(new RecipeReadPayload(string.Empty, ["2 plums"], []));

        Assert.True(model.NeedsTitle);
        Assert.Equal(ScanConfirmModel.TitleSentence, model.Problem);

        model.SetTitle("  Plum toast ");
        Assert.False(model.NeedsTitle);
        Assert.Null(model.Problem);
        Assert.Equal("Plum toast", model.Title);
    }

    [Fact]
    public void ALineTooLongForARecipeCardIsNamedNotKept()
    {
        var payload = new RecipeReadPayload("Plum toast", [new string('x', Ingredient.MaxNameLength + 1)], []);

        var model = ScanConfirmModel.From(payload);

        Assert.NotNull(model.Problem);
    }

    [Fact]
    public void KeepingBuildsAScannedCardFromTheWords()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        model.ChooseCuisine(Cuisine.American);
        model.SetServings(6);
        var keeper = new RecordingKeeper();

        var result = model.Keep(keeper, NoneYet);

        Assert.Equal(ScanKeepOutcome.Kept, result.Outcome);
        var card = Assert.Single(keeper.Kept);
        Assert.Equal(card, result.Recipe);
        Assert.Equal("apple-crisp", card.Id.Value);
        Assert.Equal("Apple crisp", card.Title);
        Assert.Equal(Cuisine.American, card.Cuisine);
        Assert.Equal(6, card.Servings);
        Assert.Equal(RecipeSource.Scanned, card.SourceLabel);
        Assert.False(card.ForKids);
        Assert.Equal(4, card.Ingredients.Count);
        Assert.Equal(new Ingredient("¾", "cup", "brown sugar"), card.Ingredients[1]);
        Assert.Equal(40, card.Steps[1].Minutes);
        Assert.False(card.Steps[1].Active);
    }

    [Fact]
    public void TheIdNeverCollidesWithACardAlreadyInTheBox()
    {
        var taken = new HashSet<RecipeId> { new("apple-crisp"), new("apple-crisp-2") };
        var keeper = new RecordingKeeper();

        ScanConfirmModel.From(ScanSamples.AppleCrisp()).Keep(keeper, taken);

        Assert.Equal("apple-crisp-3", Assert.Single(keeper.Kept).Id.Value);
    }

    [Theory]
    [InlineData("¡Crème Brûlée!", "creme-brulee")]
    [InlineData("Grandma’s apple crisp", "grandmas-apple-crisp")]
    [InlineData("!!!", "scanned-card")]
    [InlineData("  Plum   toast  ", "plum-toast")]
    public void TheIdIsMadeFromTheTitle(string title, string id)
    {
        Assert.Equal(id, ScanRecipeIds.For(title, NoneYet).Value);
    }

    [Fact]
    public void ALongTitleStillMakesAValidId()
    {
        var id = ScanRecipeIds.For(string.Join(' ', Enumerable.Repeat("marigold", 20)), new HashSet<RecipeId>());

        Assert.True(id.Value.Length <= RecipeId.MaxLength);
        Assert.DoesNotContain("--", id.Value, StringComparison.Ordinal);
        Assert.False(id.Value.EndsWith('-'));
    }
}
