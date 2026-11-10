using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Storyworthtelling.Paper.Binder;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;

namespace Index.Tests.Worksheet;

/// <summary>
/// Index's tablet pages wear the frame every tablet document shares (owner's decision): the header signed "Index" in
/// the owner's hand over the identity rule, one binder tab per page as a real link, and the folio led by Folio's
/// maker's mark — on the tablet's whole glass, with the content clear of the tab strip.
/// </summary>
public class IndexFrameTests
{
    private static WorksheetPlan Plan(int categories) => new(
        "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194,
        Enumerable.Range(1, categories).Select(i => new WorksheetCategoryLine(Guid.NewGuid(), $"Category {i}", 50m)).ToList(),
        [new WorksheetSetAsideLine(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m)]);

    private static FollowUpPlan FollowUp() => new(
        "2026-10", new DateOnly(2026, 10, 2), true, 333, 3_100, 3_433,
        [new FollowUpSuggestion("category:a1", "Trimmed", null, 101, 201, 101)], []);

    [Fact]
    public void TheBoardIsTheFramesOnTheWholeGlass()
    {
        Assert.Equal(SheetFrame.Margin, WorksheetRegions.MarginLeft);
        Assert.Equal(SheetFrame.Margin, WorksheetRegions.MarginTop);
        Assert.Equal(BinderStrip.Gutter, WorksheetRegions.MarginRight);
        Assert.Equal(IndexSheet.Page.DesignWidth, WorksheetRegions.PageWidth, 0.05f);
        Assert.Equal(IndexSheet.Page.DesignHeight, WorksheetRegions.PageHeight, 0.05f);
        Assert.Equal((0f, 0f), (IndexSheet.Page.OffsetX, IndexSheet.Page.OffsetY));
    }

    [Fact]
    public void TheWorksheetIsSignedAndTabbed()
    {
        var bytes = WorksheetPainter.Paint(Plan(10));
        using var pdf = PdfDocument.Open(bytes);

        var page = Assert.Single(pdf.GetPages());
        Assert.Contains("October 2026 budget", Words(page));
        Assert.Contains("Index", page.Text);
        Assert.Single(page.GetAnnotations(), a => a.Type == AnnotationType.Link);
        Assert.Contains("Budget", page.Text);
        Assert.Contains("/Dest /index-worksheet-1", System.Text.Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void ATwoSheetWorksheetCarriesBothTabsOnBothSheets()
    {
        var bytes = WorksheetPainter.Paint(Plan(30));
        using var pdf = PdfDocument.Open(bytes);

        Assert.Equal(2, pdf.NumberOfPages);
        foreach (var page in pdf.GetPages())
        {
            Assert.Equal(2, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
            Assert.Contains("Budget", page.Text);
            Assert.Contains("Continued", page.Text);
        }
        var raw = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Dest /index-worksheet-2", raw);
    }

    [Fact]
    public void TheFollowUpIsSignedAndTabbed()
    {
        var bytes = FollowUpPainter.Paint(FollowUp());
        using var pdf = PdfDocument.Open(bytes);

        var page = Assert.Single(pdf.GetPages());
        Assert.Contains("Index", page.Text);
        Assert.Single(page.GetAnnotations(), a => a.Type == AnnotationType.Link);
        Assert.Contains("Follow-up", page.Text);
        Assert.Contains(FollowUpPainter.Heading("2026-10"), Words(page));
    }

    [Fact]
    public void EveryPageCarriesTheMakersMarkAndNothingRunsUnderTheTabs()
    {
        using var worksheet = PdfDocument.Open(WorksheetPainter.Paint(Plan(30)));
        using var followUp = PdfDocument.Open(FollowUpPainter.Paint(FollowUp()));
        var stripLeft = WorksheetRegions.PageWidth - BinderStrip.TabWidth - BinderStrip.Hairline;

        foreach (var page in worksheet.GetPages().Concat(followUp.GetPages()))
        {
            // The mark leads the folio, in the bottom margin at the left margin: a small drawn oval, not a picture.
            Assert.Empty(page.GetImages());
            Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is { } r
                && Math.Abs(PdfProbe.BoardX((float)r.Left) - SheetFrame.Margin) < 3
                && PdfProbe.BoardY((float)r.Top) > WorksheetRegions.PageHeight - SheetFrame.Margin
                && PdfProbe.Board(r.Width) is > 6 and < 16);

            var under = page.Letters
                .Where(l => PdfProbe.BoardX((float)l.GlyphRectangle.Right) > stripLeft && Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) < 0.01) // a tab's label runs down it; page text runs across
                .Select(l => l.Value)
                .ToList();
            Assert.True(under.Count == 0, $"page {page.Number}: '{string.Concat(under)}' runs under the tabs");
        }
    }

    private static string Words(UglyToad.PdfPig.Content.Page page) => string.Join(" ", page.GetWords().Select(w => w.Text));
}
