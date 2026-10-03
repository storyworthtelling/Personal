namespace Index.Domain.Worksheet;

/// <summary>One writable box on the worksheet, in page points from the top-left. Mirrors Foreword's
/// <c>WritingRegion</c> (<c>JournalRegions</c>) — same shape, same reason: the page is painted from
/// these numbers and the read-back is told the same numbers, so the painter and the reader cannot
/// disagree about where a box is.</summary>
public readonly record struct WorksheetBox(string Name, float Left, float Top, float Width, float Height, int Page = 0);

/// <summary>
/// Where every writable box on the monthly worksheet sits (mockup 12, Release F), in points, on the
/// suite's A4 page (595 &#215; 842, margins 52.8/48/62.4 — Index's own copy of these numbers, matching
/// <c>Storyboarder/Folio.Domain/Report/PageGeometry.cs</c> exactly rather than referencing a different
/// app's type, per this task's brief).
///
/// <para><b>Both the painter (<c>WorksheetPainter</c>) and the read-back service
/// (<c>WorksheetReadBackInstruction</c>/<c>WorksheetReadBackService</c>) call the methods below —
/// neither hardcodes a box position of its own (Guard G18). The four numbers that come straight out of
/// the design book's own CSS (<c>Ledger/design/mockup-12.html</c>: <c>.ws-row</c>'s grid columns "1fr
/// 110px 150px" with a 12px gap, <c>.ws-row</c>'s 36px row height plus its 1px border, <c>.ws-box</c>'s
/// 30px height, and <c>.ws-nbox</c>'s 26px height for a write-in name box) are named as such below; the
/// vertical stacking of the header/intro/income block above the category rows has no equivalent literal
/// CSS pixel count to carry over (a flowed HTML page has no fixed offsets, only content that pushes the
/// next thing down) and is Index's own composed layout, in the same spirit as
/// <c>Foreword.Domain.Prayer.JournalRegions</c>'s own constants.</b></para>
/// </summary>
public static class WorksheetRegions
{
    public const string IncomeCorrection = "income-correction";

    public static string CategoryLimit(Guid categoryId) => $"category:{categoryId}";
    public static string NewCategoryName(int index) => $"new-name:{index}";
    public static string NewCategoryLimit(int index) => $"new-limit:{index}";

    // ---- Release H, D98/D99: the "Set aside" section's two kinds of line ----
    public const string SetAsideExtraPayment = "set-aside:extra-payment";
    public const string SetAsideSavingsPrefix = "set-aside:savings:";
    public static string SetAsideSavings(Guid savingsAccountId) => $"{SetAsideSavingsPrefix}{savingsAccountId}";

    // ---- the suite's A4 page, Index's own copy (matches Folio's PageGeometry numbers exactly) ----
    public const float PageWidth = 595f;
    public const float PageHeight = 842f;
    public const float MarginLeft = 52.8f;
    public const float MarginRight = 52.8f;
    public const float MarginTop = 48f;
    public const float MarginBottom = 62.4f;
    public const float BodyWidth = PageWidth - MarginLeft - MarginRight;

    // ---- taken verbatim from mockup-12.html's own CSS ----
    public const float ColumnGap = 12f;
    public const float LastMonthColumnWidth = 110f;
    public const float BoxColumnWidth = 150f;
    public const float NameColumnWidth = BodyWidth - LastMonthColumnWidth - BoxColumnWidth - 2 * ColumnGap;

    // D157: the Release H page is denser than Release F's — 30pt rows with 26pt boxes (still over
    // 9mm to write in), so the new "Set aside" section fits without a second sheet at a realistic
    // category count. This is the single production worksheet now (Release H extends, not forks,
    // Release F's page), so the constants below are amended in place rather than kept as two parallel
    // sets of numbers.
    public const float RowHeight = 30f;
    private const float RowBorder = 1f;
    public const float RowPitch = RowHeight + RowBorder;
    public const float BoxHeight = 26f;
    public const float NameBoxHeight = 22f;

    /// <summary>D151/D157: two blank write-in lines at the foot (down from three on Release F's less
    /// dense page), so the set-aside section still fits on one sheet at a realistic category count.</summary>
    public const int NewCategoryLines = 2;

    // ---- Index's own composed vertical layout (not a CSS transcription — see the type's own remarks).
    // Exposed (not private) so WorksheetPainter aligns its printed labels with these same offsets
    // rather than keeping a second, hand-eyeballed copy of where each block starts. ----
    public const float HeaderHeight = 54f;        // the Serif label + title + rule
    public const float IntroHeight = 34f;         // the intro sentence
    public const float IncomeBlockHeight = 92f;   // the calc rows + the correction box
    public const float SectionHeaderHeight = 20f; // ".ws-head" / ".ws-sub"

    public static float IncomeBlockTop => MarginTop + HeaderHeight + IntroHeight;

    private static float BoxLeft => MarginLeft + NameColumnWidth + ColumnGap + LastMonthColumnWidth + ColumnGap;

    public static float CategoryHeaderTop => IncomeBlockTop + IncomeBlockHeight;

    private static float CategoriesTop => CategoryHeaderTop + SectionHeaderHeight;

    /// <summary>Release H: where the "Set aside" section header sits — directly after the last category
    /// row, exactly where "ADD A CATEGORY" used to sit on Release F's page (mockup 13(a): categories,
    /// then Set aside, then Add a category).</summary>
    public static float SetAsideHeaderTop(int categoryRowCount) => CategoriesTop + categoryRowCount * RowPitch;

    private static float SetAsideRowsTop(int categoryRowCount) => SetAsideHeaderTop(categoryRowCount) + SectionHeaderHeight;

    /// <summary>Where "ADD A CATEGORY" sits: directly after the category rows when there is no set-aside
    /// section (a plan built before Release H, or a test with none — the exact position Release F always
    /// used), or after the whole "Set aside" section (its header plus its rows) when there is one.</summary>
    public static float AddCategoryHeaderTop(int categoryRowCount, int setAsideLineCount = 0) =>
        setAsideLineCount == 0
            ? SetAsideHeaderTop(categoryRowCount)
            : SetAsideRowsTop(categoryRowCount) + setAsideLineCount * RowPitch;

    /// <summary>The top of the i-th category row's text baseline area (before the box's own vertical
    /// centering) — the same starting point <see cref="CategoryLimitBoxes"/> computes each box's row
    /// from, so the painter's printed name/last-month text lines up with its own box.</summary>
    public static float CategoryRowTop(int index) => CategoriesTop + index * RowPitch;

    public static float NewCategoryRowTop(int categoryRowCount, int index, int setAsideLineCount = 0) =>
        AddCategoryHeaderTop(categoryRowCount, setAsideLineCount) + SectionHeaderHeight + index * RowPitch;

    /// <summary>The "Income different?" box, top-right of the income summary block — a fixed position,
    /// independent of how many categories the month has.</summary>
    public static WorksheetBox IncomeCorrectionBox()
    {
        return new WorksheetBox(
            IncomeCorrection,
            MarginLeft + BodyWidth - BoxColumnWidth,
            IncomeBlockTop + 14f,
            BoxColumnWidth,
            BoxHeight);
    }

    // ---- The continuation sheet (D157): the geometry WorksheetPainter used to compute for itself. ----

    /// <summary>Baseline of the one-line intro on a continuation sheet (just under the header rule).</summary>
    public const float ContinuationIntroBaseline = MarginTop + HeaderHeight - 8f + 16f;

    /// <summary>Baseline of the CATEGORY / LAST MONTH header on a continuation sheet that carries
    /// categories.</summary>
    public const float ContinuationHeaderBaseline = ContinuationIntroBaseline + 24f;

    /// <summary>Where the first row starts on the continuation sheet. A sheet with no categories on it
    /// (every category fitted on page one and only the set-aside unit moved) has no intro line and no
    /// CATEGORY header, so its first section starts where the intro would have.</summary>
    public static float ContinuationRowsTop(int categoryRowCount) =>
        categoryRowCount > 0 ? ContinuationHeaderBaseline + SectionHeaderHeight : ContinuationIntroBaseline;

    /// <summary>Where "SET ASIDE" sits on the continuation sheet, after its <paramref name="categoryRowCount"/>
    /// category rows.</summary>
    public static float ContinuationSetAsideHeaderTop(int categoryRowCount) =>
        ContinuationRowsTop(categoryRowCount) + categoryRowCount * RowPitch;

    /// <summary>Where "ADD A CATEGORY" sits on the continuation sheet.</summary>
    public static float ContinuationAddCategoryHeaderTop(int categoryRowCount, int setAsideLineCount) =>
        AddCategoryHeaderTopFrom(ContinuationSetAsideHeaderTop(categoryRowCount), setAsideLineCount);

    private static float AddCategoryHeaderTopFrom(float setAsideHeaderTop, int setAsideLineCount) =>
        setAsideLineCount == 0
            ? setAsideHeaderTop
            : setAsideHeaderTop + SectionHeaderHeight + setAsideLineCount * RowPitch;

    // ---- The box builders. Each takes the Y its section starts at and the page it is on, so page one
    // and the continuation sheet are the same arithmetic from a different origin. ----

    private static IReadOnlyList<WorksheetBox> CategoryBoxesFrom(IReadOnlyList<Guid> ids, float rowsTop, int page)
    {
        var boxes = new List<WorksheetBox>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            boxes.Add(new WorksheetBox(
                CategoryLimit(ids[i]), BoxLeft, rowsTop + i * RowPitch + (RowHeight - BoxHeight) / 2, BoxColumnWidth, BoxHeight, page));
        }
        return boxes;
    }

    private static IReadOnlyList<WorksheetBox> SetAsideBoxesFrom(float setAsideHeaderTop, IReadOnlyList<string> keys, int page)
    {
        var boxes = new List<WorksheetBox>(keys.Count);
        var rowsTop = setAsideHeaderTop + SectionHeaderHeight;
        for (var i = 0; i < keys.Count; i++)
        {
            boxes.Add(new WorksheetBox(
                keys[i], BoxLeft, rowsTop + i * RowPitch + (RowHeight - BoxHeight) / 2, BoxColumnWidth, BoxHeight, page));
        }
        return boxes;
    }

    private static IReadOnlyList<(WorksheetBox Name, WorksheetBox Limit)> NewCategoryBoxesFrom(float addHeaderTop, int page)
    {
        var addTop = addHeaderTop + SectionHeaderHeight;
        var result = new List<(WorksheetBox, WorksheetBox)>(NewCategoryLines);
        for (var i = 0; i < NewCategoryLines; i++)
        {
            var rowTop = addTop + i * RowPitch;
            var name = new WorksheetBox(
                NewCategoryName(i), MarginLeft, rowTop + (RowHeight - NameBoxHeight) / 2, NameColumnWidth, NameBoxHeight, page);
            var limit = new WorksheetBox(
                NewCategoryLimit(i), BoxLeft, rowTop + (RowHeight - BoxHeight) / 2, BoxColumnWidth, BoxHeight, page);
            result.Add((name, limit));
        }
        return result;
    }

    /// <summary>One limit box per active category on PAGE ONE, in the exact printed order — the same
    /// order the painter draws the rows in and the order the delivery service records so a later
    /// read-back can reconstruct which box belonged to which category even if the live category list has
    /// since changed (see <c>BudgetMonth.WorksheetCategoryOrderJson</c>). For a plan that runs onto a
    /// second sheet use <see cref="Layout"/>, which places the overflow where the painter draws it.</summary>
    public static IReadOnlyList<WorksheetBox> CategoryLimitBoxes(IReadOnlyList<Guid> categoryIdsInPrintedOrder) =>
        CategoryBoxesFrom(categoryIdsInPrintedOrder, CategoriesTop, page: 0);

    /// <summary>D151's (name box, limit box) pairs at the foot of PAGE ONE, positioned after every
    /// category row and — Release H — after the "Set aside" section too, when there is one.</summary>
    public static IReadOnlyList<(WorksheetBox Name, WorksheetBox Limit)> NewCategoryBoxes(int categoryRowCount, int setAsideLineCount = 0) =>
        NewCategoryBoxesFrom(AddCategoryHeaderTop(categoryRowCount, setAsideLineCount), page: 0);

    /// <summary>D98/D99: one box per set-aside line on PAGE ONE, in the exact printed order — the extra
    /// debt payment first, then one per savings goal.</summary>
    public static IReadOnlyList<WorksheetBox> SetAsideBoxes(int categoryRowCount, IReadOnlyList<string> setAsideKeysInPrintedOrder) =>
        SetAsideBoxesFrom(SetAsideHeaderTop(categoryRowCount), setAsideKeysInPrintedOrder, page: 0);

    /// <summary>
    /// Every writable box on every sheet of this plan, and where each section starts — the one answer
    /// <c>WorksheetPainter</c> draws from and <c>WorksheetReadBackInstruction</c> describes (Guard G18,
    /// extended to a plan that runs onto a second sheet). A plan that fits on one sheet lays out exactly
    /// as it always has (every box <c>Page</c> 0); one that does not puts the categories that did not fit,
    /// and the whole set-aside unit with the write-in lines, on page 1 at the continuation geometry.
    /// </summary>
    public static WorksheetPageLayout Layout(
        IReadOnlyList<Guid> categoryIdsInPrintedOrder, IReadOnlyList<string>? setAsideKeysInPrintedOrder = null)
    {
        var setAsideKeys = setAsideKeysInPrintedOrder ?? [];
        var split = WorksheetPagination.Compute(categoryIdsInPrintedOrder.Count, setAsideKeys.Count);

        if (!split.NeedsSecondPage)
        {
            var addHeader = AddCategoryHeaderTop(categoryIdsInPrintedOrder.Count, setAsideKeys.Count);
            return new WorksheetPageLayout(
                split,
                IncomeCorrectionBox(),
                CategoryLimitBoxes(categoryIdsInPrintedOrder),
                SetAsideBoxes(categoryIdsInPrintedOrder.Count, setAsideKeys),
                NewCategoryBoxes(categoryIdsInPrintedOrder.Count, setAsideKeys.Count),
                SetAsidePage: 0,
                SetAsideHeaderTop: SetAsideHeaderTop(categoryIdsInPrintedOrder.Count),
                AddCategoryHeaderTop: addHeader);
        }

        var onPageOne = categoryIdsInPrintedOrder.Take(split.CategoryRowsOnPageOne).ToList();
        var onPageTwo = categoryIdsInPrintedOrder.Skip(split.CategoryRowsOnPageOne).ToList();
        var setAsideHeaderTop = ContinuationSetAsideHeaderTop(onPageTwo.Count);

        var categories = new List<WorksheetBox>(CategoryBoxesFrom(onPageOne, CategoriesTop, page: 0));
        categories.AddRange(CategoryBoxesFrom(onPageTwo, ContinuationRowsTop(onPageTwo.Count), page: 1));

        return new WorksheetPageLayout(
            split,
            IncomeCorrectionBox(),
            categories,
            SetAsideBoxesFrom(setAsideHeaderTop, setAsideKeys, page: 1),
            NewCategoryBoxesFrom(AddCategoryHeaderTopFrom(setAsideHeaderTop, setAsideKeys.Count), page: 1),
            SetAsidePage: 1,
            SetAsideHeaderTop: setAsideHeaderTop,
            AddCategoryHeaderTop: AddCategoryHeaderTopFrom(setAsideHeaderTop, setAsideKeys.Count));
    }

    /// <summary>Every writable box on every sheet, in the order they are printed: the income correction
    /// box, one limit box per category, one per set-aside line (Release H), then the write-in
    /// (name, limit) pairs. Each box carries the page it is on.</summary>
    public static IReadOnlyList<WorksheetBox> AllBoxes(
        IReadOnlyList<Guid> categoryIdsInPrintedOrder, IReadOnlyList<string>? setAsideKeysInPrintedOrder = null) =>
        Layout(categoryIdsInPrintedOrder, setAsideKeysInPrintedOrder).All;

    // ---- D157: "with more categories than this, the page runs onto a second sheet rather than
    // shrinking further" ----

    /// <summary>The Y coordinate, in points, immediately below the last thing printed on the page (the
    /// second write-in line's own row) — content below this falls outside the printable body.</summary>
    public static float ContentBottom(int categoryRowCount, int setAsideLineCount) =>
        AddCategoryHeaderTop(categoryRowCount, setAsideLineCount) + SectionHeaderHeight + NewCategoryLines * RowPitch;

    /// <summary>Whether the whole page — categories, the "Set aside" section and the two write-in
    /// lines — fits above the bottom margin at this row pitch. False is what forces a second sheet
    /// (<see cref="WorksheetPagination"/>) rather than shrinking the rows further.</summary>
    public static bool FitsOnOnePage(int categoryRowCount, int setAsideLineCount) =>
        ContentBottom(categoryRowCount, setAsideLineCount) <= PageHeight - MarginBottom;

    /// <summary>How many category rows fit, on their own (no set-aside section, no write-in lines yet),
    /// starting a fresh page at <paramref name="firstRowTop"/> — what <see cref="WorksheetPagination"/>
    /// uses to decide how many categories page one can hold before the rest spill to page two.</summary>
    public static int MaxCategoryRowsStartingAt(float firstRowTop)
    {
        var available = PageHeight - MarginBottom - firstRowTop;
        return available <= 0 ? 0 : (int)(available / RowPitch);
    }
}

/// <summary>Where everything writable on a (possibly two-sheet) worksheet is, as <see cref="WorksheetRegions.Layout"/>
/// computed it. <see cref="SetAsidePage"/> is the sheet the set-aside section and the write-in lines are
/// on; the two header tops are on that sheet.</summary>
public sealed record WorksheetPageLayout(
    WorksheetPageSplit Split,
    WorksheetBox IncomeCorrection,
    IReadOnlyList<WorksheetBox> Categories,
    IReadOnlyList<WorksheetBox> SetAside,
    IReadOnlyList<(WorksheetBox Name, WorksheetBox Limit)> NewCategories,
    int SetAsidePage,
    float SetAsideHeaderTop,
    float AddCategoryHeaderTop)
{
    public int PageCount => Split.NeedsSecondPage ? 2 : 1;

    public IReadOnlyList<WorksheetBox> All
    {
        get
        {
            var all = new List<WorksheetBox> { IncomeCorrection };
            all.AddRange(Categories);
            all.AddRange(SetAside);
            foreach (var (name, limit) in NewCategories)
            {
                all.Add(name);
                all.Add(limit);
            }
            return all;
        }
    }
}
