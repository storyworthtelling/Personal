using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// Only the hard words are asked (book p.14, D63, D99). With no judge there are none, and the card still shows once with a single
/// "Looks right". Where a judge does doubt a word, the best reading is already chosen and the line changes in place when another is.
/// </summary>
public class ScanReadTests
{
    private static ScanConfirmModel Doubtful() => ScanConfirmModel.From(ScanSamples.AppleCrisp(), new FakeJudge(ScanSamples.ThreeDoubts()));

    [Fact]
    public void OnlyUncertainWordsAreAsked()
    {
        var model = Doubtful();

        Assert.Equal(3, model.Questions.Count);
        Assert.Equal(["Fat", "Oven", "Sugar"], model.Questions.Select(question => question.Label).Order().ToArray());
        Assert.Equal(3, model.Lines.SelectMany(line => line.Segments).Count(segment => segment.Marked));
        Assert.All(model.Lines.Where(line => line.Segments.All(segment => !segment.Marked)), line => Assert.Single(line.Segments));
    }

    [Fact]
    public void TheQuestionsFollowTheCardNotTheJudge()
    {
        var model = Doubtful();

        Assert.Equal(["Sugar", "Fat", "Oven"], model.Questions.Select(question => question.Label).ToArray());
    }

    [Fact]
    public void CleanScanShowsOnce()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), new NoRecipeWordJudge());

        Assert.Empty(model.Questions);
        Assert.Null(model.QuestionHeading);
        Assert.Equal(6, model.Lines.Count);
        Assert.All(model.Lines, line => Assert.DoesNotContain(line.Segments, segment => segment.Marked));
        Assert.Equal("Looks right", model.KeepAction);
        Assert.Empty(keeper.Kept);

        Assert.Equal(ScanKeepOutcome.Kept, model.Keep(keeper, new HashSet<RecipeId>()).Outcome);
        Assert.Single(keeper.Kept);
    }

    [Fact]
    public void NoJudgeIsTheDefaultAndOffersNothing()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        Assert.Empty(model.Questions);
        Assert.Empty(new NoRecipeWordJudge().Judge(ScanSamples.AppleCrisp()));
    }

    [Fact]
    public void TheJudgeIsAskedOnceAboutTheWordsAlone()
    {
        var judge = new FakeJudge([]);

        ScanConfirmModel.From(ScanSamples.AppleCrisp(), judge);

        Assert.Equal(1, judge.Calls);
        Assert.Equal(ScanSamples.AppleCrisp().Title, judge.Asked!.Title);
    }

    [Fact]
    public void TheBestReadingIsChosenAndTheLinesAreUnchanged()
    {
        var model = Doubtful();

        Assert.All(model.Questions, question => Assert.Equal(0, question.Selected));
        Assert.Equal(["350°F", "330°F", "380°F"], model.Questions.Single(question => question.Label == "Oven").Options.ToArray());
        Assert.Equal("Oven to 350°F", model.Steps[0].Text);
        Assert.Equal("Fat", model.Questions[1].Label);
        Assert.Equal(["butter", "batter"], model.Questions[1].Options.ToArray());
    }

    [Fact]
    public void ChoosingAnAlternativeChangesTheLineInPlace()
    {
        var model = Doubtful();
        var oven = IndexOf(model, "Oven");

        Assert.True(model.Choose(oven, 1));

        Assert.Equal("Oven to 330°F", model.Steps[0].Text);
        Assert.Equal(1, model.Questions[oven].Selected);
        var line = model.Lines.Single(candidate => candidate.Kind == ScanLineKind.Step && candidate.Index == 0);
        Assert.Contains(line.Segments, segment => segment.Marked && segment.Text == "330°F");
    }

    [Fact]
    public void AChosenAmountReachesTheIngredientDraft()
    {
        var model = Doubtful();

        model.Choose(IndexOf(model, "Sugar"), 1);

        Assert.Equal(new ScanIngredientDraft("¼", "cup", "brown sugar"), model.Ingredients[1]);
    }

    [Fact]
    public void OtherTakesTheOwnersTypedWords()
    {
        var model = Doubtful();
        var fat = IndexOf(model, "Fat");

        Assert.True(model.ChooseOther(fat, "  cold butter "));

        Assert.Equal(ScanQuestion.OtherIndex, model.Questions[fat].Selected);
        Assert.Equal("cold butter", model.Questions[fat].OtherText);
        Assert.Equal("½ cup cold butter, cold", model.Lines.Single(line => line.Kind == ScanLineKind.Ingredient && line.Index == 3).Text);
    }

    [Fact]
    public void ABlankOtherAndAWildChoiceChangeNothing()
    {
        var model = Doubtful();
        var fat = IndexOf(model, "Fat");

        Assert.False(model.ChooseOther(fat, "   "));
        Assert.False(model.Choose(fat, 9));
        Assert.False(model.Choose(-1, 0));
        Assert.False(model.Choose(9, 0));
        Assert.False(model.ChooseOther(9, "x"));

        Assert.Equal(0, model.Questions[fat].Selected);
        Assert.Equal("½ cup butter, cold", model.Ingredients[3].Quantity + " " + model.Ingredients[3].Unit + " " + model.Ingredients[3].Name);
    }

    [Fact]
    public void ADoubtAboutAWordNotOnTheLineIsDropped()
    {
        var judge = new FakeJudge(
        [
            new UncertainWord(ScanLineKind.Step, 0, "Oven", "425°F", ["400°F"]),
            new UncertainWord(ScanLineKind.Step, 9, "Nowhere", "350°F", ["330°F"]),
            new UncertainWord(ScanLineKind.Ingredient, -1, "Nowhere", "butter", ["batter"]),
            new UncertainWord(ScanLineKind.Ingredient, 3, "  ", "", ["batter"]),
        ]);

        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), judge);

        Assert.Empty(model.Questions);
    }

    [Fact]
    public void TwoAlternativesAtMostAndNeverTheReadingAgain()
    {
        var judge = new FakeJudge([new UncertainWord(ScanLineKind.Step, 0, "Oven", "350°F", ["350°f", "330°F", "330°f", "380°F", "400°F"])]);

        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), judge);

        Assert.Equal(["350°F", "330°F", "380°F"], model.Questions.Single().Options.ToArray());
    }

    [Fact]
    public void TwoWordsOnOneLineBothChange()
    {
        var judge = new FakeJudge(
        [
            new UncertainWord(ScanLineKind.Ingredient, 3, "Amount", "½", ["¼"]),
            new UncertainWord(ScanLineKind.Ingredient, 3, "Fat", "butter", ["batter"]),
        ]);
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), judge);

        model.Choose(0, 1);
        model.Choose(1, 1);

        Assert.Equal(new ScanIngredientDraft("¼", "cup", "batter, cold"), model.Ingredients[3]);
    }

    [Fact]
    public void OverlappingDoubtsKeepTheFirst()
    {
        var judge = new FakeJudge(
        [
            new UncertainWord(ScanLineKind.Ingredient, 1, "Sugar", "¾ cup", ["¼ cup"]),
            new UncertainWord(ScanLineKind.Ingredient, 1, "Cup", "cup", ["pint"]),
        ]);

        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), judge);

        Assert.Equal("Sugar", Assert.Single(model.Questions).Label);
    }

    [Theory]
    [InlineData(1, "One word was hard to read")]
    [InlineData(2, "Two words were hard to read")]
    [InlineData(3, "Three words were hard to read")]
    [InlineData(10, "Ten words were hard to read")]
    [InlineData(11, "11 words were hard to read")]
    public void TheHeadingCountsTheDoubts(int count, string heading)
    {
        Assert.Equal(heading, ScanQuestionText.Heading(count));
    }

    [Fact]
    public void ImagesAreNeverStored()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), new FakeJudge(ScanSamples.ThreeDoubts()));
        model.Choose(0, 2);
        model.ChooseOther(1, "salted butter");

        model.Keep(keeper, new HashSet<RecipeId>());

        var card = Assert.Single(keeper.Kept);
        var everyWord = new[] { card.Title, card.SourceLabel ?? string.Empty }
            .Concat(card.Ingredients.SelectMany(ingredient => new[] { ingredient.Quantity ?? string.Empty, ingredient.Unit ?? string.Empty, ingredient.Name }))
            .Concat(card.Steps.Select(step => step.Text));
        Assert.All(everyWord, word =>
        {
            Assert.DoesNotContain("data:", word, StringComparison.Ordinal);
            Assert.DoesNotContain("content://", word, StringComparison.Ordinal);
            Assert.DoesNotContain("file://", word, StringComparison.Ordinal);
            Assert.DoesNotContain(".jpg", word, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(RecipeSource.Scanned, card.SourceLabel);
    }

    private static int IndexOf(ScanConfirmModel model, string label) =>
        model.Questions.Select((question, index) => (question, index)).Single(pair => pair.question.Label == label).index;
}
