using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>What Plate's answer comes to when it comes back (C-6.5.1, D99): the words, a quiet cancel, or a sentence that names the field and never the value.</summary>
public class ScanReturnTests
{
    [Fact]
    public void ABackedOutScanIsQuiet()
    {
        var result = ScanReturns.Decide(false, "Apple crisp", "[\"2 apples\"]", null);

        Assert.IsType<ScanReturn.Cancelled>(result);
    }

    [Fact]
    public void AGoodAnswerIsThePayloadAndNothingElse()
    {
        var result = ScanReturns.Decide(true, "Apple crisp", "[\"2 apples\",\"¾ cup sugar\"]", "[\"Oven to 350°F\"]");

        var read = Assert.IsType<ScanReturn.Read>(result);
        Assert.Equal("Apple crisp", read.Payload.Title);
        Assert.Equal(["2 apples", "¾ cup sugar"], read.Payload.Ingredients);
        Assert.Equal(["Oven to 350°F"], read.Payload.Steps);
    }

    [Fact]
    public void AMalformedListNamesTheFieldNotTheValue()
    {
        var result = ScanReturns.Decide(true, "Apple crisp", "{\"secret\":1}", null);

        var refused = Assert.IsType<ScanReturn.Refused>(result);
        Assert.Contains("ingredients", refused.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", refused.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("Apple", refused.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardWithNothingOnItSaysSo()
    {
        var result = ScanReturns.Decide(true, "Apple crisp", "[]", "[]");

        var refused = Assert.IsType<ScanReturn.Refused>(result);
        Assert.Equal(ScanReturns.NothingOnTheCardSentence, refused.Sentence);
    }

    [Fact]
    public void ARequestAimsAtPlateAndCarriesNothing()
    {
        Assert.Equal(RecipeReadContract.Action, ScanRequest.Action);
        Assert.Equal(RecipeReadContract.PlatePackage, ScanRequest.Package);
        Assert.Equal(RecipeReadContract.Activity, ScanRequest.Component);
        Assert.StartsWith(ScanRequest.Package + ".", ScanRequest.Component, StringComparison.Ordinal);
    }
}
