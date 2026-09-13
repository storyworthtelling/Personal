using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// The cover page and the contents — the front matter, which is unnumbered so the content folios can
/// be 1..N and the printed contents needs no bookkeeping.
/// </summary>
public class ReportCoverTests
{
    private static readonly DateTimeOffset Read = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    private static string FirstPage(ReportCover cover, BookAnalysisResult? analysis = null)
    {
        var pdf = new AnalysisDocumentRenderer()
            .Render(analysis ?? SampleAnalysis.Of(), cover, Array.Empty<SceneEvidence>());
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return document.GetPage(1).Text;
    }

    [Fact]
    public void TheCoverPrintsTheDraftItRead()
    {
        var text = FirstPage(new ReportCover("The Salt Line", 3, 94_200, Read, null, null));

        Assert.Contains("The Salt Line", text, StringComparison.Ordinal);
        Assert.Contains("Draft three", text, StringComparison.Ordinal);
        Assert.Contains("94,200 words", text, StringComparison.Ordinal);
        Assert.Contains("12 September 2026", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCoverSaysWhereASequelSitsInItsSeries()
    {
        var text = FirstPage(new ReportCover("The Salt Line", 1, 0, Read, null, "Book one of three · The Salt Line"));

        Assert.Contains("Book one of three", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStandaloneBookSaysNothingAboutASeries()
    {
        var text = FirstPage(new ReportCover("The Salt Line", 1, 0, Read, null, null));

        Assert.DoesNotContain("Book ", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A draft with no measured length prints the draft alone rather than "0 words", which would be a
    /// statement about the manuscript rather than about what the counter knew.
    /// </summary>
    [Fact]
    public void AnUnmeasuredDraftDoesNotClaimToBeEmpty()
    {
        var text = FirstPage(new ReportCover("The Salt Line", 1, 0, Read, null, null));

        Assert.Contains("Draft one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 words", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongContentsPaginatesRatherThanRunningOffTheSheet()
    {
        var many = SampleAnalysis.Of(
            characters: Enumerable.Range(1, 40).Select(i => SampleAnalysis.Character($"Character {i}")).ToList(),
            plotHoles: Enumerable.Range(1, 40).Select(i => new ReportFinding($"Ch. {i}", "A hole.")).ToList());

        var result = new AnalysisDocumentRenderer().RenderReport(
            many, new ReportCover("The Salt Line", 1, 0, Read, null, null), Array.Empty<SceneEvidence>());

        // Cover plus at least one contents page, and never more pages of front matter than entries.
        Assert.True(result.FrontMatterCount >= 2, "The front matter lost its contents page.");
        Assert.True(result.FrontMatterCount <= result.TocEntries.Count + 1);
    }

    [Fact]
    public void TheFrontMatterIsUnnumbered()
    {
        var result = new AnalysisDocumentRenderer().RenderReport(
            SampleAnalysis.Of(),
            new ReportCover("The Salt Line", 1, 0, Read, null, null), Array.Empty<SceneEvidence>());

        using var document = PdfDocument.Open(new MemoryStream(result.Pdf));
        for (var page = 1; page <= result.FrontMatterCount; page++)
        {
            var text = document.GetPage(page).Text;
            Assert.DoesNotContain("The Salt Line1", text.Replace(" ", string.Empty), StringComparison.Ordinal);
        }

        // …and the first content page carries folio 1.
        var first = document.GetPage(result.FrontMatterCount + 1).Text;
        Assert.Contains("1", first, StringComparison.Ordinal);
    }
}
