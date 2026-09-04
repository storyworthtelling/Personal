using DailyLoader.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace DailyLoader.Tests.Pdf;

// Direct coverage of the Skia layout maths — the blind-risk core of the DailyLoader rewrite. Appearance is
// checked on device (the Drive test plan); these lock wrapping, pagination, measurement and truncation.
public class SkiaPageLayoutTests
{
    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return PdfReader.Open(stream, PdfDocumentOpenMode.Import).PageCount;
    }

    [Fact]
    public void EmptyLayoutProducesOneValidPdfPage()
    {
        using var layout = new SkiaPageLayout();
        var pdf = layout.ToPdf();

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(1, PageCount(pdf));
    }

    [Fact]
    public void PortraitAndLandscapeHaveSwappedDimensions()
    {
        using var portrait = new SkiaPageLayout();
        using var landscape = new SkiaPageLayout(landscape: true);

        Assert.Equal(612, portrait.PageWidth);
        Assert.Equal(792, portrait.PageHeight);
        Assert.Equal(792, landscape.PageWidth);
        Assert.Equal(612, landscape.PageHeight);
    }

    [Fact]
    public void NewPageAdvancesThePageCountAndResetsCursor()
    {
        using var layout = new SkiaPageLayout();
        Assert.Equal(1, layout.PageCountInProgress);

        layout.Advance(200);
        layout.NewPage();

        Assert.Equal(2, layout.PageCountInProgress);
        Assert.Equal(SkiaPageLayout.Margin, layout.Y);
        Assert.Equal(2, PageCount(layout.ToPdf()));
    }

    [Fact]
    public void WrappedTextOverflowsOntoNewPages()
    {
        using var layout = new SkiaPageLayout();
        var font = SkiaPageLayout.Sans(12);
        var paragraph = string.Join(' ', Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 80));

        // One long paragraph is far taller than a page, so it must paginate.
        layout.DrawWrapped(paragraph, font);

        Assert.True(layout.PageCountInProgress > 1);
    }

    [Fact]
    public void NewPageIfNeededOnlyBreaksWhenSpaceIsShort()
    {
        using var layout = new SkiaPageLayout();
        layout.NewPageIfNeeded(10);              // plenty of room at the top
        Assert.Equal(1, layout.PageCountInProgress);

        layout.MoveTo(layout.BottomY - 5);
        layout.NewPageIfNeeded(50);              // won't fit → break
        Assert.Equal(2, layout.PageCountInProgress);
    }

    [Fact]
    public void TruncateAddsEllipsisOnlyWhenTooWide()
    {
        using var layout = new SkiaPageLayout();
        var font = SkiaPageLayout.Sans(10);

        Assert.Equal("short", layout.Truncate("short", font, 500));

        var truncated = layout.Truncate(new string('W', 200), font, 60);
        Assert.EndsWith("…", truncated);
        Assert.True(layout.MeasureText(truncated, font) <= 60);
    }

    [Fact]
    public void BoldTextMeasuresWiderThanRegularAtTheSameSize()
    {
        using var layout = new SkiaPageLayout();
        var regular = layout.MeasureText("Handwriting", SkiaPageLayout.Sans(20));
        var bold = layout.MeasureText("Handwriting", SkiaPageLayout.Sans(20, bold: true));
        Assert.True(bold > regular);
    }
}
