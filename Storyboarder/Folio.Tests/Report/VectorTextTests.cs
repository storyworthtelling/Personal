using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Report;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// G14 — the report's text is text.
///
/// <para>The bug this reintroduces is rendering a page as a bitmap and wrapping it, which is what the
/// report did until this release. Nothing in the old suite could tell the difference: a page of type
/// and a photograph of a page of type are both one valid PDF page, so the test that checked
/// <c>PageCount &gt; 0</c> passed either way. What was actually lost was everything a reader does
/// with a report — search it for a character's name, copy a line into the manuscript, let the tablet
/// render the type at the panel's own resolution instead of resampling a 150-DPI raster onto it.</para>
/// </summary>
public class VectorTextTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private static byte[] Report(BookAnalysisResult analysis) =>
        new AnalysisDocumentRenderer().Render(analysis, Cover, Array.Empty<SceneEvidence>());

    private static BookAnalysisResult Analysis() => SampleAnalysis.Of();

    [Fact]
    public void TheReportIsAPdf()
    {
        var pdf = Report(Analysis());

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void TheReportCarriesRealTextNotAPictureOfIt()
    {
        using var document = PdfDocument.Open(new MemoryStream(Report(Analysis())));

        var text = string.Concat(document.GetPages().Select(page => page.Text));
        Assert.Contains("Saltings", text, StringComparison.Ordinal);
        Assert.Contains("The Salt Line", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageIsA4InPoints()
    {
        using var document = PdfDocument.Open(new MemoryStream(Report(Analysis())));

        foreach (var page in document.GetPages())
        {
            Assert.Equal(PageGeometry.PageWidth, page.Width, 0);
            Assert.Equal(PageGeometry.PageHeight, page.Height, 0);
        }
    }

    /// <summary>
    /// The contents names the page a section actually starts on — which is the whole reason the
    /// content is rendered twice, and the thing that silently goes wrong if the two passes disagree.
    /// </summary>
    [Fact]
    public void TheContentsAgreesWithWhereTheSectionsLand()
    {
        var analysis = Analysis();
        var result = new AnalysisDocumentRenderer()
            .RenderReport(analysis, Cover, Array.Empty<SceneEvidence>());

        Assert.NotEmpty(result.TocEntries);
        using var document = PdfDocument.Open(new MemoryStream(result.Pdf));

        foreach (var (title, page) in result.TocEntries)
        {
            var printed = document.GetPage(result.FrontMatterCount + page).Text;
            Assert.Contains(Squashed(title), Squashed(printed), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The size claim, measured rather than cited — and measured as the <b>per-page</b> cost, which is
    /// the only form of it that is true.
    ///
    /// <para>The first version of this test asserted a total, and failed at 760 KB on a two-page
    /// report. The cause was not rasterisation: a PDF embeds each face it uses once, at roughly 75 KB,
    /// and this document sets seven — Serif in four weights, Sans in two, and the mono the folio is
    /// figures in. That is a fixed cost paid by the first page and by no other, so on a short document
    /// the vector file is <i>larger</i> than the raster one, exactly as the shared library's own sink
    /// test found. What actually replaced 150-DPI pages is the slope: a page of type costs about a
    /// kilobyte here, against the hundreds a 1240 × 1754 bitmap costs whatever is drawn on it.</para>
    /// </summary>
    [Fact]
    public void AddingAPageCostsAPageOfTypeRatherThanAPageOfPixels()
    {
        var small = Report(Analysis());
        var large = new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of(
                characters: Enumerable.Range(1, 60).Select(i => SampleAnalysis.Character($"Character {i}")).ToList(),
                plotHoles: Enumerable.Range(1, 60).Select(i => new ReportFinding($"Ch. {i}", "A hole in the plot.")).ToList()),
            Cover, Array.Empty<SceneEvidence>());

        int Pages(byte[] pdf)
        {
            using var document = PdfDocument.Open(new MemoryStream(pdf));
            return document.NumberOfPages;
        }

        var added = Pages(large) - Pages(small);
        Assert.True(added > 3, "The larger report did not actually run to more pages.");

        var perPage = (large.Length - small.Length) / (double)added;
        Assert.True(perPage < 8_000,
            $"A page costs {perPage:N0} bytes, which is bitmap territory — check the pages are not being "
            + "rasterised on their way into the sink.");
    }

    private static string Squashed(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
