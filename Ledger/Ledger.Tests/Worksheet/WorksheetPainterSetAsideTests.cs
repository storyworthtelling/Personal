using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>Release H: the worksheet's "Set aside" section, the zero-based intro sentence, and the
/// pagination fallback — proven the same way the rest of this pipeline is, by reading the vector text
/// back out of the produced PDF (never by parsing drawing operators).</summary>
public class WorksheetPainterSetAsideTests
{
    private static Domain.Worksheet.WorksheetPlan MockupThirteenAPlan()
    {
        var categories = new[] { "Groceries", "Dining out", "Fuel", "Household", "Kids", "Utilities", "Subscriptions", "Medical", "Clothing", "Gifts" }
            .Select(name => new WorksheetCategoryLine(Guid.NewGuid(), name, 100m))
            .ToList();

        var setAside = new List<WorksheetSetAsideLine>
        {
            new(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m),
            new(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", 225m),
            new(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Summer trip", 135m),
        };

        return new Domain.Worksheet.WorksheetPlan(
            "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories, setAside);
    }

    [Fact]
    public void SetAsideSection_PrintsOnOnePage_WithEveryLineAndTheZeroBasedTarget()
    {
        var plan = MockupThirteenAPlan();

        var pdfBytes = WorksheetPainter.Paint(plan);

        using var document = PdfDocument.Open(new MemoryStream(pdfBytes));
        Assert.Equal(1, document.NumberOfPages); // mockup 13(a)'s own realistic example stays on one sheet

        var text = document.GetPage(1).Text;
        Assert.Contains("SET ASIDE", text, StringComparison.Ordinal);
        Assert.Contains("Extra debt payment", text, StringComparison.Ordinal);
        Assert.Contains("to Meridian Card", text, StringComparison.Ordinal);
        Assert.Contains("Savings", text, StringComparison.Ordinal);
        Assert.Contains("Emergency fund", text, StringComparison.Ordinal);
        Assert.Contains("Summer trip", text, StringComparison.Ordinal);
        // D100/D155's own target: income ($9,350) less Snowball payments ($6,250) = $3,100.
        Assert.Contains("$3,100", text, StringComparison.Ordinal);
        Assert.Contains("ADD A CATEGORY", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxesFor_IncludesOneBoxPerSetAsideLine()
    {
        var plan = MockupThirteenAPlan();

        var boxes = WorksheetPainter.BoxesFor(plan);

        Assert.Contains(boxes, b => b.Name == WorksheetRegions.SetAsideExtraPayment);
        Assert.Equal(2, boxes.Count(b => b.Name.StartsWith("set-aside:savings:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ManyCategories_OverflowsToASecondPage_AndBothPagesAreExtractableText()
    {
        var manyCategories = Enumerable.Range(1, 30)
            .Select(i => new WorksheetCategoryLine(Guid.NewGuid(), $"Category {i}", 50m))
            .ToList();
        var setAside = new List<WorksheetSetAsideLine>
        {
            new(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m),
        };
        var plan = new Domain.Worksheet.WorksheetPlan(
            "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, manyCategories, setAside);

        var pdfBytes = WorksheetPainter.Paint(plan);

        using var document = PdfDocument.Open(new MemoryStream(pdfBytes));
        Assert.Equal(2, document.NumberOfPages);

        var page1Text = document.GetPage(1).Text;
        var page2Text = document.GetPage(2).Text;

        Assert.Contains("Category 1", page1Text, StringComparison.Ordinal);
        Assert.Contains("Continued on the next sheet", page1Text, StringComparison.Ordinal);
        Assert.Contains("Category 30", page2Text, StringComparison.Ordinal);
        Assert.Contains("SET ASIDE", page2Text, StringComparison.Ordinal); // the set-aside section moved as one unit
        Assert.Contains("ADD A CATEGORY", page2Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanWithNoSetAsideLines_PrintsTheOriginalReleaseFIntro_NotTheZeroBasedOne()
    {
        var categories = new[] { new WorksheetCategoryLine(Guid.NewGuid(), "Groceries", 600m) };
        var plan = new Domain.Worksheet.WorksheetPlan(
            "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories);

        var pdfBytes = WorksheetPainter.Paint(plan);

        using var document = PdfDocument.Open(new MemoryStream(pdfBytes));
        var text = document.GetPage(1).Text;

        Assert.DoesNotContain("SET ASIDE", text, StringComparison.Ordinal);
        Assert.Contains("Add a category on a blank line at the foot", text, StringComparison.Ordinal);
    }
}
