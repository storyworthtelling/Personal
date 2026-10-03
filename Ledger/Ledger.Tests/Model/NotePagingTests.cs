using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.2 item 4 (mockup 7/8): the post-it's "3 more" and the history note's "Earlier months"
/// were printed and could not be touched. Each now lengthens the note in place, so the question the
/// page has to ask is arithmetic: how many rows are showing after N taps.
/// </summary>
public class NotePagingTests
{
    [Theory]
    [InlineData(7, 0, 4)]    // mockup 7: four rows and "3 more"
    [InlineData(7, 1, 7)]    // one tap shows the rest
    [InlineData(25, 0, 4)]
    [InlineData(25, 1, 14)]
    [InlineData(25, 2, 24)]
    [InlineData(25, 3, 25)]  // never past the total
    [InlineData(25, 9, 25)]
    [InlineData(2, 0, 2)]    // fewer than the first page: all of them, nothing to tap
    [InlineData(0, 0, 0)]
    public void TransactionsGrowByATapAndStopAtTheTotal(int total, int taps, int expectedShown) =>
        Assert.Equal(expectedShown, NotePaging.Shown(total, NotePaging.TransactionsInitial, NotePaging.TransactionsStep, taps));

    [Theory]
    [InlineData(8, 0, 6)]    // mockup 8: six months and "Earlier months"
    [InlineData(8, 1, 8)]
    [InlineData(30, 0, 6)]
    [InlineData(30, 1, 18)]  // a year further back per tap
    [InlineData(30, 2, 30)]
    public void HistoryGrowsByAYearATapAndStopsAtTheTotal(int total, int taps, int expectedShown) =>
        Assert.Equal(expectedShown, NotePaging.Shown(total, NotePaging.HistoryInitial, NotePaging.HistoryStep, taps));

    [Theory]
    [InlineData(7, 0, 3)]
    [InlineData(7, 1, 0)]
    [InlineData(25, 1, 11)]
    public void RemainingIsWhatIsStillHidden(int total, int taps, int expected) =>
        Assert.Equal(expected, NotePaging.Remaining(total, NotePaging.TransactionsInitial, NotePaging.TransactionsStep, taps));

    [Fact]
    public void ANegativeTapCountIsNoTaps() =>
        Assert.Equal(4, NotePaging.Shown(20, 4, 10, -3));

    [Fact]
    public void TheMoreLabelIsTheMockupsOwnWords()
    {
        Assert.Equal("3 more", NotePaging.MoreLabel(3));
        Assert.Equal("11 more", NotePaging.MoreLabel(11));
    }
    // ---- remediation 3.3 item 2: the ACCOUNT post-it (mockup 3: "Five show, and 17 more lengthens the note in place") ----

    [Theory]
    [InlineData(22, 0, 5, 17)]   // mockup 3, to the word: five rows, "17 more"
    [InlineData(22, 1, 15, 7)]   // one tap: ten more rows
    [InlineData(22, 2, 22, 0)]   // never past the total; nothing left to tap
    [InlineData(5, 0, 5, 0)]     // exactly one page: no "more" at all
    [InlineData(3, 0, 3, 0)]
    [InlineData(140, 0, 5, 135)] // the fetch is no longer capped at 30, so "more" can exceed 25
    public void TheAccountPostItShowsFiveThenTenMorePerTap(int total, int taps, int shown, int remaining)
    {
        Assert.Equal(5, NotePaging.AccountTransactionsInitial);
        Assert.Equal(10, NotePaging.TransactionsStep);
        Assert.Equal(shown, NotePaging.Shown(total, NotePaging.AccountTransactionsInitial, NotePaging.TransactionsStep, taps));
        Assert.Equal(remaining, NotePaging.Remaining(total, NotePaging.AccountTransactionsInitial, NotePaging.TransactionsStep, taps));
    }

    [Fact]
    public void TheFetchAsksPlaidForAsManyAsItWillReturnInOneCall()
    {
        // Plaid's /transactions/get returns at most 500 per request (it pages by offset beyond that). Thirty
        // days of one account is far inside 500; the old count of 30 capped "n more" at 25.
        Assert.Equal(500, NotePaging.TransactionFetchCap);
        Assert.Equal(30, NotePaging.TransactionWindowDays);
    }

    [Fact]
    public void TheTapCountBelongsToTheOpenCard_AndResetsWhenAnotherOpens()
    {
        var redwood = Guid.NewGuid();
        var meridian = Guid.NewGuid();
        var taps = new OpenNoteTaps();

        taps.Opened(redwood);
        taps.Tapped(redwood);
        taps.Tapped(redwood);
        Assert.Equal(2, taps.For(redwood));

        taps.Opened(meridian); // opening another card moves the post-it (D128)
        Assert.Equal(0, taps.For(meridian));
        Assert.Equal(0, taps.For(redwood));

        taps.Opened(redwood);    // and coming back starts it again
        Assert.Equal(0, taps.For(redwood));
    }

    [Fact]
    public void ClosingTheCardResetsItsTaps()
    {
        var redwood = Guid.NewGuid();
        var taps = new OpenNoteTaps();
        taps.Opened(redwood);
        taps.Tapped(redwood);

        taps.Opened(null);
        taps.Opened(redwood);

        Assert.Equal(0, taps.For(redwood));
    }

    [Fact]
    public void ReopeningTheSameCardKeepsItsTaps_ARenderIsNotAnOpening()
    {
        var redwood = Guid.NewGuid();
        var taps = new OpenNoteTaps();
        taps.Opened(redwood);
        taps.Tapped(redwood);

        taps.Opened(redwood);

        Assert.Equal(1, taps.For(redwood));
    }
}
