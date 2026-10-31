using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>What Constitution says when Plate comes back with nothing, with a back-out, or with a long card read across many pages (C-6.5.1, D99).</summary>
public class ScanSentenceTests
{
    private static string Lines(string prefix, int count) =>
        "[" + string.Join(',', Enumerable.Range(0, count).Select(n => "\"" + prefix + " " + n + "\"")) + "]";

    [Fact]
    public void ABackOutWithNoCardSaysNothingWasKept()
    {
        var sentence = ScanReturns.CancelledSentence(hasDraft: false);

        Assert.Equal("Plate closed before reading a card, so nothing was kept.", sentence);
    }

    [Fact]
    public void ABackOutOverADraftSaysTheCardIsAsItWas()
    {
        var sentence = ScanReturns.CancelledSentence(hasDraft: true);

        Assert.Equal("Plate closed without a new card, so the card on the page is as it was.", sentence);
    }

    [Fact]
    public void AnAnswerWithNoExtrasIsACardWithNothingOnIt()
    {
        var refused = Assert.IsType<ScanReturn.Refused>(ScanReturns.Decide(true, null, null, null));

        Assert.Equal(ScanReturns.NothingOnTheCardSentence, refused.Sentence);
    }

    [Fact]
    public void AnswerFromManyPagesIsOneRead()
    {
        var read = Assert.IsType<ScanReturn.Read>(ScanReturns.Decide(true, "Long stew", Lines("ingredient", 120), Lines("step", 80)));

        Assert.Equal(120, read.Payload.Ingredients.Count);
        Assert.Equal("ingredient 0", read.Payload.Ingredients[0]);
        Assert.Equal("ingredient 119", read.Payload.Ingredients[^1]);
        Assert.Equal(80, read.Payload.Steps.Count);
    }

    [Fact]
    public void ARunawayCardIsRefusedNamingTheField()
    {
        var refused = Assert.IsType<ScanReturn.Refused>(
            ScanReturns.Decide(true, "Long stew", Lines("ingredient", RecipeReadContract.MaxLines + 1), null));

        Assert.Contains("ingredients", refused.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("ingredient 7", refused.Sentence, StringComparison.Ordinal);
    }
}
