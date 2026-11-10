using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.Imaging;
using Folio.Domain.Report;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;

namespace Folio.Tests.Report;

/// <summary>
/// The revision report wears the frame every tablet document shares (owner's decision): the tablet's own page, the
/// book's title signed "Folio" in the owner's hand over the identity rule, a binder tab per section of the report as a
/// real link to where it starts, and the folio led by the maker's mark. The cover keeps its jacket and carries the
/// strip with no tab open.
/// </summary>
public class FolioFrameTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    // A report with readers, a plan and a verdict as well as its frame: four parts, four tabs. No manuscript was given.
    private static AnalysisRenderResult Report() =>
        new AnalysisDocumentRenderer().RenderReport(
            SampleAnalysis.Of() with
            {
                Reviews = [.. PersonaCatalogue.Readers.Select(persona => new Review(
                    persona.Id, "It holds.", "A read in character, two sentences long. It names the middle.",
                    ["The opening."], ["The middle."], "A shorter middle."))],
                RevisionPlan = [new RevisionItem("High", "Chapter 1", "Sagging middle", "Cut a subplot.")],
                Verdict = new Verdict(["a strength"], ["a problem"], "the middle compressed"),
            },
            Cover, Array.Empty<SceneEvidence>());

    private static readonly string[] Present = ["Frame", "Readers", "Plan", "Verdict"];

    private static string Vertical(UglyToad.PdfPig.Content.Page page) =>
        string.Concat(page.Letters.Where(l => Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > 0.01).Select(l => l.Value));

    [Fact]
    public void TheBoardIsA4sHeightOnTheWholeGlassInTheFramesMargins()
    {
        Assert.Equal(FolioSheet.Page.DesignWidth, PageGeometry.PageWidth, 0.05f);
        Assert.Equal(FolioSheet.Page.DesignHeight, PageGeometry.PageHeight, 0.05f);
        Assert.Equal((0f, 0f), (FolioSheet.Page.OffsetX, FolioSheet.Page.OffsetY));
        Assert.Equal(SheetFrame.Margin, PageGeometry.ContentLeft);
        Assert.Equal(BinderStrip.ContentRight(PageGeometry.PageWidth), PageGeometry.ContentRight, 0.05f);
        Assert.Equal(SheetFrame.ContentBottom(FolioSheet.Page), PageGeometry.ContentBottom, 0.05f);
        Assert.True(PageGeometry.ContentTop > SheetFrame.Margin + SheetFrame.HeaderHeight + SheetFrame.RuleGap + SheetFrame.RuleHeight);
    }

    [Fact]
    public void EveryPageIsTheTabletsPage()
    {
        using var pdf = PdfDocument.Open(Report().Pdf);

        Assert.All(pdf.GetPages(), page =>
        {
            Assert.Equal(RemarkablePage.Width, (float)page.Width, 1f);
            Assert.Equal(RemarkablePage.Height, (float)page.Height, 1f);
        });
    }

    [Fact]
    public void EveryPageCarriesATabForEachSectionTheReportHas()
    {
        using var pdf = PdfDocument.Open(Report().Pdf);

        foreach (var page in pdf.GetPages())
        {
            var labels = Vertical(page);
            Assert.Equal(Present.Length, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
            foreach (var label in Present)
            {
                Assert.Contains(label, labels);
            }
            // Parts that did not print have no tab: no manuscript was given, and nothing was left for the notes.
            Assert.DoesNotContain("Manuscript", labels);
            Assert.DoesNotContain("Notes", labels);
        }
    }

    [Fact]
    public void EachTabLandsWhereItsSectionStarts()
    {
        var report = Report();
        using var pdf = PdfDocument.Open(report.Pdf);

        var targets = pdf.GetPage(2).GetAnnotations()
            .Where(a => a.Type == AnnotationType.Link)
            .OrderByDescending(a => a.Rectangle.Top)
            .Select(a => Assert.IsAssignableFrom<AbstractGoToAction>(a.Action).Destination.PageNumber)
            .ToList();

        Assert.Contains("Theframe", pdf.GetPage(targets[0]).Text.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, targets[0]); // cover, contents, then the frame
        Assert.Contains("readers", pdf.GetPage(targets[1]).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What to do about it", pdf.GetPage(targets[2]).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Biggest strengths", pdf.GetPage(targets[3]).Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryPageAfterTheCoverIsSignedAndItsFolioCarriesTheMark()
    {
        using var pdf = PdfDocument.Open(Report().Pdf);
        var scale = FolioSheet.Page.Scale;

        foreach (var page in pdf.GetPages().Skip(1))
        {
            var script = string.Concat(page.Letters.Where(l => (l.FontName ?? "").Contains("Pinyon", StringComparison.Ordinal)).Select(l => l.Value));
            Assert.Contains("Folio", script);
            Assert.Contains("The Salt Line", page.Text);
            Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is { } r
                && Math.Abs(r.Left - SheetFrame.Margin * scale) < 3
                && r.Top < page.Height - SheetFrame.ContentBottom(FolioSheet.Page) * scale
                && r.Width is > 4 and < 12);
        }
    }

    /// <summary>
    /// No sheet carries only its frame. The manuscript used to turn a sheet after its last line and the readers turn
    /// their own, which left a page with a header, tabs and a folio and nothing else between them.
    /// </summary>
    [Fact]
    public void NoSheetIsOnlyItsFrame()
    {
        var prose = string.Join("\n\n", Enumerable.Range(1, 30).Select(i =>
            $"Ruth stood at the sea wall and did not look back at the house. Paragraph {i} goes on for a line or two more."));
        var pdf = new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with
            {
                LineEdits = [new LineEdit("Chapter 1", "did not look back at the house", "Show her walking away.", "Weaker than the action.")],
                Reviews = [.. PersonaCatalogue.Readers.Select(persona => new Review(
                    persona.Id, "It holds.", "A read in character.", ["The opening."], ["The middle."], "A shorter middle."))],
            },
            Cover, Array.Empty<SceneEvidence>(), manuscript: prose);

        using var document = PdfDocument.Open(pdf);
        var top = (float)document.GetPage(1).Height - PageGeometry.ContentTop * FolioSheet.Page.Scale;
        var foot = (float)document.GetPage(1).Height - PageGeometry.ContentBottom * FolioSheet.Page.Scale;
        foreach (var page in document.GetPages())
        {
            var content = page.GetWords().Where(word => word.BoundingBox.Top < top + 1 && word.BoundingBox.Bottom > foot - 1
                && word.BoundingBox.Right < page.Width - 30).ToList();
            Assert.True(content.Count > 0, $"page {page.Number} carries only its frame");
        }
    }
}
