using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Storyworthtelling.Paper;
using UglyToad.PdfPig;

namespace Index.Tests.Worksheet;

/// <summary>
/// The worksheet and the follow-up page are the tablet's own page (audit 01 §12): drawn on the A4 board the mockups
/// are measured in, placed on 445 × 594 with one scale, and the read-back is told every box in that page's points.
/// </summary>
public class TabletPageTests
{
    private static WorksheetPlan Plan() => new(
        "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194,
        [new WorksheetCategoryLine(Guid.NewGuid(), "Groceries", 50m)], []);

    private static FollowUpPlan FollowUp() => new(
        "2026-10", new DateOnly(2026, 10, 2), true, 333, 3_100, 3_433,
        [new FollowUpSuggestion("category:a1", "Trimmed", null, 101, 201, 101)], []);

    [Fact]
    public void BothPagesAreTheTabletsPage()
    {
        using var worksheet = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(Plan())));
        using var followUp = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(FollowUp())));

        foreach (var page in worksheet.GetPages().Concat(followUp.GetPages()))
        {
            Assert.Equal(RemarkablePage.Width, (float)page.Width, 1f);
            Assert.Equal(RemarkablePage.Height, (float)page.Height, 1f);
        }
    }

    [Fact]
    public void TheBoardIsTheMockupsA4AndFitsTheGlass()
    {
        Assert.Equal((WorksheetRegions.PageWidth, WorksheetRegions.PageHeight), (IndexSheet.Page.DesignWidth, IndexSheet.Page.DesignHeight));
        Assert.Equal(RemarkablePage.Height / WorksheetRegions.PageHeight, IndexSheet.Page.Scale, 4);
    }

    [Fact]
    public void BothPromptsGiveThePagesOwnSizeAndBoxes()
    {
        var plan = Plan();
        var worksheet = WorksheetReadBackInstruction.Build(plan.CategoryIdsInPrintedOrder, plan.SetAsideKeysInPrintedOrder);
        var followUp = FollowUpReadBackInstruction.Build([("category:a1", "Trimmed")], 0);

        Assert.Contains("445.0 by 594.0 points", worksheet, StringComparison.Ordinal);
        Assert.Contains("445.0 by 594.0 points", followUp, StringComparison.Ordinal);

        var income = PdfProbe.OnPage(WorksheetRegions.IncomeCorrectionBox());
        Assert.Contains($"({income.Left:0.0}, {income.Top:0.0}, {income.Width:0.0}, {income.Height:0.0})", worksheet, StringComparison.Ordinal);
    }
}
