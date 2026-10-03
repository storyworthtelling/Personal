using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G18 (INDEX-P11-PLAN.md) extended to a plan that runs onto a second sheet (remediation §2.1.4).
/// The painter laid page two out with its own arithmetic while <see cref="WorksheetRegions.AllBoxes"/>
/// and <see cref="WorksheetReadBackInstruction"/> described a single page, so any category past page one,
/// and every set-aside / write-in box on page two, was described to the vision model at coordinates
/// that do not exist on the painted page. The read-back would then either read ink from the wrong place
/// or call a blank box blank.
///
/// <para>Asserted on what was painted: the PDF is rendered and every stroked box-sized rectangle on each
/// page is read back out with PdfPig. The painter, the region list and the prompt must then agree on
/// every box's page and coordinates, and nothing painted may be a box nobody was told about.</para>
/// </summary>
public class WorksheetTwoPageRegionsTests
{
    private static Domain.Worksheet.WorksheetPlan MakePlan(int categoryCount, int setAsideLineCount)
    {
        var categories = Enumerable.Range(1, categoryCount)
            .Select(i => new WorksheetCategoryLine(Guid.NewGuid(), $"Category {i}", 50m)).ToList();
        var setAside = new List<WorksheetSetAsideLine>();
        if (setAsideLineCount > 0)
        {
            setAside.Add(new(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m));
        }
        for (var i = 1; i < setAsideLineCount; i++)
        {
            setAside.Add(new(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", $"Goal {i}", 100m));
        }
        return new Domain.Worksheet.WorksheetPlan(
            "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories, setAside);
    }

    public static TheoryData<int, int, int> Plans => new()
    {
        // categories, set-aside lines, expected number of pages
        { 10, 3, 1 },  // mockup 13(a): one sheet
        { 30, 3, 2 },  // categories spill onto page two, and the set-aside unit with them
        { 15, 3, 2 },  // every category fits on page one; the set-aside unit alone moves to page two
    };

    private static List<(float Left, float Top, float Width, float Height)> PaintedBoxes(UglyToad.PdfPig.Content.Page page) =>
        page.Paths
            .Select(p => p.GetBoundingRectangle())
            .Where(r => r.HasValue && r.Value.Height >= 20 && r.Value.Height <= 30 && r.Value.Width > 20)
            .Select(r => (
                Left: (float)r!.Value.Left,
                Top: WorksheetRegions.PageHeight - (float)r.Value.Top,
                Width: (float)r.Value.Width,
                Height: (float)r.Value.Height))
            .ToList();

    private static bool Matches((float Left, float Top, float Width, float Height) painted, WorksheetBox box) =>
        Math.Abs(painted.Left - box.Left) <= 1.5f && Math.Abs(painted.Top - box.Top) <= 1.5f
        && Math.Abs(painted.Width - box.Width) <= 1.5f && Math.Abs(painted.Height - box.Height) <= 1.5f;

    [Theory]
    [MemberData(nameof(Plans))]
    public void EveryBoxIsPaintedOnThePageTheRegionsSayItIsOn_AndNothingElseIsPaintedAsABox(int categories, int setAside, int pages)
    {
        var plan = MakePlan(categories, setAside);
        var boxes = WorksheetPainter.BoxesFor(plan);

        using var document = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));
        Assert.Equal(pages, document.NumberOfPages);

        for (var pageIndex = 0; pageIndex < pages; pageIndex++)
        {
            var painted = PaintedBoxes(document.GetPage(pageIndex + 1));
            var expected = boxes.Where(b => b.Page == pageIndex).ToList();

            foreach (var box in expected)
            {
                Assert.True(
                    painted.Any(p => Matches(p, box)),
                    $"{box.Name} is described at ({box.Left:0.#}, {box.Top:0.#}, {box.Width:0.#}, {box.Height:0.#}) on page {pageIndex + 1} but nothing is painted there");
            }
            Assert.True(
                painted.Count == expected.Count,
                $"page {pageIndex + 1} paints {painted.Count} boxes but {expected.Count} are described");
        }
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void ThePromptNamesEveryBoxWithItsPageAndTheExactSameCoordinates(int categories, int setAside, int pages)
    {
        var plan = MakePlan(categories, setAside);
        var instruction = WorksheetReadBackInstruction.Build(plan.CategoryIdsInPrintedOrder, plan.SetAsideKeysInPrintedOrder);

        foreach (var box in WorksheetPainter.BoxesFor(plan))
        {
            var line = $"\"{box.Name}\" at ({box.Left:0.0}, {box.Top:0.0}, {box.Width:0.0}, {box.Height:0.0})";
            Assert.True(instruction.Contains(line, StringComparison.Ordinal), $"the prompt does not describe {line}");
            var lineText = instruction.Split('\n').Single(l => l.Contains(line, StringComparison.Ordinal));
            Assert.Contains($"page {box.Page + 1}", lineText, StringComparison.Ordinal);
        }
        if (pages > 1)
        {
            Assert.Contains($"{pages} pages", instruction, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void EveryDescribedBoxIsInsideThePrintableBodyOfItsOwnPage(int categories, int setAside, int pages)
    {
        var plan = MakePlan(categories, setAside);

        foreach (var box in WorksheetPainter.BoxesFor(plan))
        {
            Assert.InRange(box.Page, 0, pages - 1);
            Assert.True(
                box.Top + box.Height <= WorksheetRegions.PageHeight - WorksheetRegions.MarginBottom,
                $"{box.Name} on page {box.Page + 1} runs past the bottom margin ({box.Top + box.Height:0.#})");
        }
    }

    [Fact]
    public void WhenEveryCategoryFitsOnPageOne_PageTwoIsNotGivenAnEmptyCategoryTable()
    {
        // 15 categories fit on the first sheet; the set-aside unit does not, so it alone goes to page
        // two. That page must not announce "the rest of this month's categories" over a CATEGORY /
        // LAST MONTH header with no rows under it.
        var plan = MakePlan(15, 3);

        using var document = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));
        Assert.Equal(2, document.NumberOfPages);
        var pageTwo = document.GetPage(2).Text;

        Assert.DoesNotContain("The rest of this month's categories", pageTwo, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST MONTH", pageTwo, StringComparison.Ordinal);
        Assert.Contains("SET ASIDE", pageTwo, StringComparison.Ordinal);
        Assert.Contains("ADD A CATEGORY", pageTwo, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenCategoriesSpill_PageTwoKeepsItsCategoryTable()
    {
        var plan = MakePlan(30, 3);

        using var document = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));
        var pageTwo = document.GetPage(2).Text;

        Assert.Contains("The rest of this month's categories", pageTwo, StringComparison.Ordinal);
        Assert.Contains("LAST MONTH", pageTwo, StringComparison.Ordinal);
    }
}
