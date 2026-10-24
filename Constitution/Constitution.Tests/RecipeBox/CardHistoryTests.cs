using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>What a picked card says about itself (C-6.2.2, book p.13), in the brief's words.</summary>
public class CardHistoryTests
{
    private static readonly DateOnly Today = BoxFixture.Today;

    [Fact]
    public void HistoryReadsInTheBriefsWords()
    {
        var history = BoxFixture.ChiliHistory();

        Assert.Equal("Last cooked 2 weeks ago · loved by Sam, Lee, Robin", CardHistoryWords.Headline(history, Today));
        Assert.Equal("Cooked 6 times since spring", CardHistoryWords.Counted(history));
        Assert.Equal("Sam, Lee, Robin: loved · Juniper: fine", CardHistoryWords.Reactions(history));
    }

    [Fact]
    public void EveryoneLovingItIsLovedByAll()
    {
        Assert.Equal("Last cooked 2 weeks ago · loved by all", CardHistoryWords.Headline(BoxFixture.AllLoved(), Today));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(3, "3 days ago")]
    [InlineData(7, "1 week ago")]
    [InlineData(14, "2 weeks ago")]
    [InlineData(55, "7 weeks ago")]
    [InlineData(90, "3 months ago")]
    [InlineData(400, "over a year ago")]
    public void WhenItWasCookedIsRelative(int daysAgo, string words)
    {
        var history = new CardHistory(Today.AddDays(-daysAgo), 1, null, []);

        Assert.Equal("Last cooked " + words, CardHistoryWords.Headline(history, Today));
    }

    [Fact]
    public void ACardNeverCookedSaysSoAndHasNoCount()
    {
        Assert.Equal("Not cooked yet", CardHistoryWords.Headline(CardHistory.Never, Today));
        Assert.Null(CardHistoryWords.Counted(CardHistory.Never));
        Assert.Null(CardHistoryWords.Reactions(CardHistory.Never));
    }

    [Fact]
    public void OnceIsOnceAndNoSeasonIsNoSince()
    {
        Assert.Equal("Cooked once", CardHistoryWords.Counted(new CardHistory(Today, 1, null, [])));
        Assert.Equal("Cooked 4 times", CardHistoryWords.Counted(new CardHistory(Today, 4, " ", [])));
    }

    [Fact]
    public void NobodyLovingItLeavesLovedOutOfTheHeadline()
    {
        var history = new CardHistory(Today.AddDays(-14), 2, null, [new CardReaction("Juniper", Reaction.Fine)]);

        Assert.Equal("Last cooked 2 weeks ago", CardHistoryWords.Headline(history, Today));
    }

    [Fact]
    public void FactsAreHandsOnTimeAndServings()
    {
        Assert.Equal("10 min hands-on · serves 6", CardHistoryWords.Facts(BoxFixture.Chili()));
        Assert.Null(CardHistoryWords.Facts(Constitution.Tests.Recipes.RecipeData.Plain("x", "X", Constitution.Domain.Recipes.Cuisine.Other)));
    }
}
