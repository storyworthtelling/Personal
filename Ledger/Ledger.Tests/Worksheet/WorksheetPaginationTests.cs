using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>D157: "with more categories than this, the page runs onto a second sheet rather than
/// shrinking further." Proves the realistic mockup 13(a) case (10 categories, 3 set-aside lines) stays
/// on one sheet, and that a large category count genuinely forces a second one.</summary>
public class WorksheetPaginationTests
{
    [Fact]
    public void MockupThirteenA_TenCategoriesThreeSetAsideLines_FitsOnOneSheet()
    {
        var split = WorksheetPagination.Compute(categoryCount: 10, setAsideLineCount: 3);

        Assert.False(split.NeedsSecondPage);
        Assert.Equal(10, split.CategoryRowsOnPageOne);
        Assert.Equal(0, split.CategoryRowsOnPageTwo);
        Assert.True(WorksheetRegions.FitsOnOnePage(10, 3));
    }

    [Fact]
    public void ManyCategories_OverflowsToASecondSheet()
    {
        // A household with this many spending categories is not realistic, but the mechanism must
        // still hold rather than silently shrinking the rows further.
        var split = WorksheetPagination.Compute(categoryCount: 30, setAsideLineCount: 3);

        Assert.True(split.NeedsSecondPage);
        Assert.Equal(30, split.CategoryRowsOnPageOne + split.CategoryRowsOnPageTwo);
        Assert.True(split.CategoryRowsOnPageTwo > 0);
        Assert.False(WorksheetRegions.FitsOnOnePage(30, 3));
    }

    [Fact]
    public void NoSetAsideLines_BehavesExactlyAsReleaseFsPageDid()
    {
        // A plan with no set-aside section (backward compatibility) never needs a second sheet at any
        // realistic category count — matching Release F's own page, which never paginated.
        var split = WorksheetPagination.Compute(categoryCount: 10, setAsideLineCount: 0);

        Assert.False(split.NeedsSecondPage);
        Assert.Equal(10, split.CategoryRowsOnPageOne);
    }

    [Fact]
    public void SecondPage_KeepsPageOnesCategoryRowsWithinTheBottomMargin()
    {
        // Page one, in the overflow case, carries ONLY category rows — no "Set aside" section, no
        // write-in lines (those move to page two as one unit) — so its own last row must sit above the
        // bottom margin on its own, with no trailing section's height budgeted in.
        var split = WorksheetPagination.Compute(categoryCount: 30, setAsideLineCount: 3);

        var lastRowBottom = WorksheetRegions.CategoryRowTop(split.CategoryRowsOnPageOne - 1) + WorksheetRegions.RowHeight;
        Assert.True(lastRowBottom <= WorksheetRegions.PageHeight - WorksheetRegions.MarginBottom);
    }
}
