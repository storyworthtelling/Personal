using Folio.Domain.Pipeline;
using Folio.Domain.Series;
using Xunit;

namespace Folio.Tests.Pipeline;

// Release R: regenerating the books already reviewed, with an invented shelf.
public class RegenerationPlanTests
{
    private static readonly Guid Series = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static RegenerationCandidate Book(string title, int written = 1, Guid? series = null, StoryPlacement? placement = null, int words = 90_000) =>
        new(Guid.NewGuid(), title, 1, words, series, written, placement);

    [Fact]
    public void ASeriesIsRegeneratedInStoryOrderSoEachBooksBibleExistsBeforeTheNext()
    {
        var road = Book("The Salt Road", 1, Series);
        var ledger = Book("The Tide Ledger", 2, Series);
        var prequel = Book("Before the Salt", 3, Series, StoryPlacement.Before("The Salt Road").Confirm());
        var between = Book("The Long Ferry", 4, Series, StoryPlacement.Between("The Salt Road", "The Tide Ledger"));

        var order = RegenerationPlan.Order([ledger, between, road, prequel]);

        Assert.Equal(["Before the Salt", "The Salt Road", "The Long Ferry", "The Tide Ledger"],
            order.Where(item => item.Book.SeriesId == Series).Select(item => item.Book.Title));
        Assert.Equal([0, 1, 2, 3], order.Where(item => item.Book.SeriesId == Series).Select(item => item.Rank));
    }

    [Fact]
    public void AStandaloneBookHasNoRankToWaitOn()
    {
        var order = RegenerationPlan.Order([Book("Marsh Lights")]);

        Assert.Equal(0, Assert.Single(order).Rank);
    }

    [Fact]
    public void StoryOrderFollowsPlacementsAndKeepsWrittenOrderOtherwise()
    {
        var order = StoryOrder.Sort(
        [
            ("A", 1, null),
            ("B", 2, null),
            ("C", 3, StoryPlacement.After("A")),
            ("D", 4, StoryPlacement.InEra("the far future")),
        ]);

        Assert.Equal(["A", "C", "B", "D"], order);
    }

    /// <summary>The estimate scales with the manuscript and is rounded to whole dollars, being only an estimate.</summary>
    [Fact]
    public void TheCostScalesWithTheWordsAndIsRoughlyStated()
    {
        var novel = RegenerationCost.Estimate(90_000);
        var story = RegenerationCost.Estimate(9_000);

        Assert.True(novel > story);
        Assert.True(novel is >= 1m and <= 10m, $"{novel}");
        Assert.Equal(novel * 2, RegenerationCost.Estimate(180_000), 0);
    }

    [Fact]
    public void TheCardListsTheBooksAndTheEstimateAndSaysWhereReportsGo()
    {
        var books = RegenerationPlan.Order([Book("Marsh Lights", words: 90_000), Book("The Salt Road", words: 45_000)]);

        var card = RegenerationCard.For(books);

        Assert.Equal("Regenerate 2 reviewed books?", card.Title);
        Assert.Contains("Marsh Lights", card.Subtitle);
        Assert.Contains("The Salt Road", card.Subtitle);
        Assert.Contains("About $", card.Subtitle);
        Assert.Contains("rough estimate", card.Subtitle);
        Assert.Contains("Drive", card.Subtitle);
        Assert.Contains("reMarkable", card.Subtitle);
        Assert.StartsWith("Regenerate 2 books", card.Action);
    }

    [Fact]
    public void WithNothingToRegenerateTheCardSaysSo()
    {
        var card = RegenerationCard.For([]);

        Assert.Equal("No reviewed books to regenerate", card.Title);
        Assert.Null(card.Action);
    }
}
