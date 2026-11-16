using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Model;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>The plan page on a later draft: each item's standing against the last, and what is now fixed (R19).</summary>
public class PlanSinceTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 2, 90_000, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private static BookAnalysisResult Analysis() => SampleAnalysis.Of() with
    {
        RevisionPlan =
        [
            new RevisionItem("Must", "Ch. 2", "The middle sags", "Cut a subplot.") { Since = "open" },
            new RevisionItem("Should", "Ch. 7", "The storm arrives unannounced", "Plant it earlier.") { Since = "new" },
        ],
        FixedSincePrevious = [new RevisionItem("Must", "Ch. 9", "The ending is rushed", "Give the last scene room.")],
    };

    [Fact]
    public void ALaterDraftsPlanSaysWhereEachItemStandsAndWhatIsFixed()
    {
        var text = TextOf(new AnalysisDocumentRenderer().Render(Analysis(), Cover, [], new DraftReview(2, [], [])));

        Assert.Contains("Still open from the last draft", text, StringComparison.Ordinal);
        Assert.Contains("New since the last draft", text, StringComparison.Ordinal);
        Assert.Contains("Fixed since the last draft", text, StringComparison.Ordinal);
        Assert.Contains("The ending is rushed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFirstDraftsPlanCarriesNoMarks()
    {
        var text = TextOf(new AnalysisDocumentRenderer().Render(Analysis(), Cover with { DraftNumber = 1 }, [], DraftReview.FirstDraft));

        Assert.DoesNotContain("last draft", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Join(" ", document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
