using Foreword.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Pdf;

// Direct coverage of the Skia layout maths — the blind-risk core of the Foreword rewrite. Appearance is
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

    /// <summary>
    /// The document carries real text, not a picture of one.
    ///
    /// <para>Every page used to be a 150-DPI PNG wrapped by PdfSharp, so nothing in a delivered
    /// document could be selected or searched and the tablet resampled it twice. Nothing in the old
    /// test suite could tell the difference — a page of text and a photograph of a page of text both
    /// counted as one valid page — which is exactly how it survived this long.</para>
    /// </summary>
    [Fact]
    public void TheDocumentCarriesRealTextNotAPictureOfIt()
    {
        using var layout = new SkiaPageLayout();
        layout.DrawWrapped("Tuesday the sixth of September", SkiaPageLayout.Sans(12));

        using var document = PdfDocument.Open(new MemoryStream(layout.ToPdf()));

        Assert.Contains("Tuesday", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Images still work on a vector page (D5). The comic and the article photographs are the two
    /// things that genuinely are pictures, and the move to vector must not quietly drop them —
    /// which it would do invisibly, since a page with a missing image is still a valid page.
    /// </summary>
    [Fact]
    public void AnImageDrawnOntoAVectorPageIsStillInTheDocument()
    {
        using var layout = new SkiaPageLayout();
        layout.DrawImage(OnePixelPng(), 54, 100, 200, 200);

        using var document = PdfDocument.Open(new MemoryStream(layout.ToPdf()));

        Assert.NotEmpty(document.GetPage(1).GetImages());
    }

    /// <summary>
    /// The vector pages still merge with the crossword PDF that arrives from the New York Times.
    /// PdfSharp reads the document Skia wrote; the finding says this chain works and this is the
    /// test that says so here, since a broken merge means no crossword and nothing else failing.
    /// </summary>
    [Fact]
    public void AVectorDocumentStillMergesWithAnotherPdf()
    {
        using var first = new SkiaPageLayout();
        first.DrawWrapped("The morning", SkiaPageLayout.Sans(12));

        using var second = new SkiaPageLayout();
        second.DrawWrapped("The crossword", SkiaPageLayout.Sans(12));

        var merged = PdfMerger.Merge(first.ToPdf(), second.ToPdf());

        Assert.Equal(2, PageCount(merged));
        using var document = PdfDocument.Open(new MemoryStream(merged));
        Assert.Contains("morning", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("crossword", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PortraitAndLandscapeHaveSwappedDimensions()
    {
        using var portrait = new SkiaPageLayout();
        using var landscape = new SkiaPageLayout(landscape: true);

        Assert.Equal(portrait.PageWidth, landscape.PageHeight, 3);
        Assert.Equal(portrait.PageHeight, landscape.PageWidth, 3);
    }

    /// <summary>
    /// The sheets are the tablet's own page (audit 01 §12), drawn in Letter's points and scaled once, so the type is
    /// the size it always looked on the glass and the board grows into the room the tablet used to leave blank.
    /// </summary>
    [Fact]
    public void ThePdfIsTheTabletsPageAndTheBoardIsAtLeastLetter()
    {
        using var portrait = new SkiaPageLayout();
        using var landscape = new SkiaPageLayout(landscape: true);
        portrait.DrawText("portrait", SkiaPageLayout.Sans(12), 54, 54, 200, 20, TextAlign.TopLeft);
        landscape.DrawText("landscape", SkiaPageLayout.Sans(12), 54, 54, 200, 20, TextAlign.TopLeft);

        Assert.Equal(612, portrait.PageWidth, 3);
        Assert.True(portrait.PageHeight >= 792);
        Assert.Equal(612, landscape.PageHeight, 3);
        Assert.True(landscape.PageWidth >= 792);

        using var tall = PdfDocument.Open(new MemoryStream(portrait.ToPdf()));
        using var wide = PdfDocument.Open(new MemoryStream(landscape.ToPdf()));
        Assert.Equal((445d, 594d), (Math.Round(tall.GetPage(1).Width), Math.Round(tall.GetPage(1).Height)));
        Assert.Equal((594d, 445d), (Math.Round(wide.GetPage(1).Width), Math.Round(wide.GetPage(1).Height)));
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

    /// <summary>A minimal valid PNG, so the image test carries no fixture file.</summary>
    private static byte[] OnePixelPng()
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(8, 8));
        surface.Canvas.Clear(SkiaSharp.SKColors.Gray);
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
