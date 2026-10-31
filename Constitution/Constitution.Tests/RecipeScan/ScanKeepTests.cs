using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// Keep only the words, and only when the owner says so (C-6.5.3). Nothing reaches the household until Looks right; discarding
/// forgets the words; the default keeper says plainly that there is no household file yet.
/// </summary>
public class ScanKeepTests
{
    private static readonly IReadOnlySet<RecipeId> NoneYet = new HashSet<RecipeId>();

    [Fact]
    public void NothingIsKeptUntilTheOwnerSaysSo()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), new FakeJudge(ScanSamples.ThreeDoubts()));

        model.Choose(0, 1);
        model.SetTitle("Grandma’s apple crisp");
        model.ChooseCuisine(Cuisine.American);
        model.SetServings(8);

        Assert.Empty(keeper.Kept);
        Assert.False(model.IsKept);
    }

    [Fact]
    public void LooksRightKeepsOnceAndOnlyOnce()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        var first = model.Keep(keeper, NoneYet);
        var second = model.Keep(keeper, NoneYet);

        Assert.Equal(ScanKeepOutcome.Kept, first.Outcome);
        Assert.Equal(ScanKeepOutcome.AlreadyKept, second.Outcome);
        Assert.Single(keeper.Kept);
        Assert.True(model.IsKept);
    }

    [Fact]
    public void WithNoHouseholdFileTheCardIsNotKeptAndSaysWhy()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        var result = model.Keep(new NoRecipeKeeper(), NoneYet);

        Assert.Equal(ScanKeepOutcome.NotKept, result.Outcome);
        Assert.Equal(NoRecipeKeeper.Sentence, result.Sentence);
        Assert.False(model.IsKept);
        Assert.Contains("household file", NoRecipeKeeper.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeeperThatRefusesLeavesTheDraftToTryAgain()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        var refused = model.Keep(new RecordingKeeper { Refusal = "The household file is locked." }, NoneYet);
        var later = new RecordingKeeper();
        var retried = model.Keep(later, NoneYet);

        Assert.Equal(ScanKeepOutcome.NotKept, refused.Outcome);
        Assert.Equal("The household file is locked.", refused.Sentence);
        Assert.Equal(ScanKeepOutcome.Kept, retried.Outcome);
        Assert.Single(later.Kept);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void AFailedWriteIsNamedByTypeAndTheDraftSurvives(Type failure)
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        var keeper = new RecordingKeeper { Throws = (Exception)Activator.CreateInstance(failure, "secret detail")! };

        var result = model.Keep(keeper, NoneYet);

        Assert.Equal(ScanKeepOutcome.NotKept, result.Outcome);
        Assert.StartsWith(ScanConfirmModel.NotKeptSentence, result.Sentence, StringComparison.Ordinal);
        Assert.EndsWith("(" + failure.Name + ")", result.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", result.Sentence!, StringComparison.Ordinal);
        Assert.False(model.IsKept);
    }

    [Fact]
    public void AnythingElseThatGoesWrongIsNotSwallowed()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        var keeper = new RecordingKeeper { Throws = new NotSupportedException("bug") };

        Assert.Throws<NotSupportedException>(() => model.Keep(keeper, NoneYet));
    }

    [Fact]
    public void ADraftWithAProblemIsNotOfferedToTheKeeper()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(new RecipeReadPayload(string.Empty, ["2 plums"], []));

        var result = model.Keep(keeper, NoneYet);

        Assert.Equal(ScanKeepOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(ScanConfirmModel.TitleSentence, result.Sentence);
        Assert.Empty(keeper.Kept);
    }

    [Fact]
    public void DiscardingForgetsEveryWord()
    {
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp(), new FakeJudge(ScanSamples.ThreeDoubts()));

        model.Discard();

        Assert.True(model.IsDiscarded);
        Assert.Equal(string.Empty, model.Title);
        Assert.Empty(model.Ingredients);
        Assert.Empty(model.Steps);
        Assert.Empty(model.Lines);
        Assert.Empty(model.Questions);
    }

    [Fact]
    public void ADiscardedDraftCannotBeKept()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        model.Discard();

        var result = model.Keep(keeper, NoneYet);

        Assert.Equal(ScanKeepOutcome.NothingToKeep, result.Outcome);
        Assert.Empty(keeper.Kept);
        Assert.False(model.Choose(0, 0));
    }

    [Fact]
    public void AKeptDraftCannotBeChangedOrDiscardedIntoNothing()
    {
        var keeper = new RecordingKeeper();
        var model = ScanConfirmModel.From(ScanSamples.AppleCrisp());
        model.Keep(keeper, NoneYet);

        model.Discard();

        Assert.False(model.IsDiscarded);
        Assert.Equal("Apple crisp", model.Title);
    }
}
