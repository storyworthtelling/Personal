namespace Index.Domain.Worksheet;

/// <summary>D157: "with more categories than this, the page runs onto a second sheet rather than
/// shrinking further." How many category rows page one holds, how many spill to page two, and whether
/// a second page is needed at all.</summary>
public readonly record struct WorksheetPageSplit(int CategoryRowsOnPageOne, int CategoryRowsOnPageTwo, bool NeedsSecondPage);

/// <summary>
/// Pure pagination decision, read by <c>WorksheetPainter</c> before it draws a stroke. The "Set aside"
/// section and the two write-in lines are kept together as one unit and never split across a sheet — a
/// savings goal's printed name and its own box must never land on different pages — so when the whole
/// page does not fit (<see cref="WorksheetRegions.FitsOnOnePage"/>), that whole unit moves to page two
/// and page one keeps as many category rows as it alone can hold.
/// </summary>
public static class WorksheetPagination
{
    public static WorksheetPageSplit Compute(int categoryCount, int setAsideLineCount)
    {
        if (WorksheetRegions.FitsOnOnePage(categoryCount, setAsideLineCount))
        {
            return new WorksheetPageSplit(categoryCount, 0, false);
        }

        var pageOneCapacity = WorksheetRegions.MaxCategoryRowsStartingAt(WorksheetRegions.CategoryRowTop(0));
        var onPageOne = Math.Min(categoryCount, pageOneCapacity);
        var onPageTwo = categoryCount - onPageOne;
        return new WorksheetPageSplit(onPageOne, onPageTwo, true);
    }
}
