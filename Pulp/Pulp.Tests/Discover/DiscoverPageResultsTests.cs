using Pulp.Domain.Core;
using Pulp.Domain.Discover;

namespace Pulp.Tests.Discover;

/// <summary>
/// Search and mood results (D172, D272 — no mockup, built from the stack's row anatomy): the top
/// twenty, "more" below; the household's own first, elsewhere after the hairline; the count line
/// reads matches of total.
/// </summary>
public class DiscoverPageResultsTests
{
    private static DiscoverRow Row(int n, bool yours) => new(
        new TitleId("title-" + n), TitleKind.Film, "Invented " + n, "Film", yours ? "On Exampleflix · HD" : "On Elsewhere TV · not yours",
        yours, null, []);

    [Fact]
    public void YoursComeFirstAndElsewhereAfterKeepingEachGroupsOrder()
    {
        var page = DiscoverPageResults.Build([Row(1, false), Row(2, true), Row(3, false), Row(4, true)], pages: 1);

        Assert.Equal(["title-2", "title-4"], page.Yours.Select(row => row.Id.Value).ToArray());
        Assert.Equal(["title-1", "title-3"], page.Elsewhere.Select(row => row.Id.Value).ToArray());
        Assert.Equal("4 of 4", page.CountLine);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void OnlyTheTopTwentyShowUntilMoreIsAskedFor()
    {
        var rows = Enumerable.Range(1, 45).Select(n => Row(n, yours: n % 3 != 0)).ToList();

        var first = DiscoverPageResults.Build(rows, pages: 1);
        var second = DiscoverPageResults.Build(rows, pages: 2);
        var third = DiscoverPageResults.Build(rows, pages: 3);

        Assert.Equal(20, first.Yours.Count + first.Elsewhere.Count);
        Assert.Equal("20 of 45", first.CountLine);
        Assert.True(first.HasMore);
        Assert.Equal("40 of 45", second.CountLine);
        Assert.Equal("45 of 45", third.CountLine);
        Assert.False(third.HasMore);
    }

    [Fact]
    public void TheTopTwentyAreTakenAfterOrderingSoYoursAreNeverPushedOffByElsewhere()
    {
        var rows = Enumerable.Range(1, 20).Select(n => Row(n, yours: false)).Append(Row(99, yours: true)).ToList();

        var page = DiscoverPageResults.Build(rows, pages: 1);

        Assert.Equal("title-99", Assert.Single(page.Yours).Id.Value);
        Assert.Equal(19, page.Elsewhere.Count);
    }

    [Fact]
    public void ARowTheSourceListedTwiceShowsOnce()
    {
        var page = DiscoverPageResults.Build([Row(1, true), Row(1, true)], pages: 1);

        Assert.Single(page.Yours);
        Assert.Equal("1 of 1", page.CountLine);
    }

    [Fact]
    public void NothingFoundIsAnEmptyPageNotAnError()
    {
        var page = DiscoverPageResults.Build([], pages: 1);

        Assert.True(page.IsEmpty);
        Assert.Equal("0 of 0", page.CountLine);
    }
}
