using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// The pacing chart on a manuscript with one chapter.
///
/// <para>Found on the first report ever composed from a real, short manuscript: a single-chapter
/// story gives the tension curve exactly one point, and a line chart has no line to draw between
/// fewer than two of them. The page still printed "Tension across the chapters — a dip is a place
/// to check the story is still pulling" and then the chart's own frame rule and lone point, both
/// pushed to the following page by the space check — an isolated dot above a bare horizontal rule,
/// with nothing on the page saying what either one is. A claim about trend needs at least two
/// chapters to trend between; below that the honest answer is to print nothing rather than a mark
/// no reader could parse.</para>
/// </summary>
public class PacingChartPlacementTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string Caption = "Tension across the chapters";

    [Fact]
    public void OneChapterOfTension_PrintsNoChart_ThereIsNothingToShowATrendBetween()
    {
        var text = TextOf(Render([new ChapterTension("1", 0.4)]));

        Assert.DoesNotContain(Caption, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoOrMoreChapters_StillPrintTheChartAndItsCaption()
    {
        var text = TextOf(Render([new ChapterTension("1", 0.4), new ChapterTension("2", 0.7)]));

        Assert.Contains(Caption, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single chapter's narrative pacing notes are still worth printing — there just isn't a
    /// trend to chart. The heading and its bullets survive even though the chart doesn't.
    /// </summary>
    [Fact]
    public void OneChapterWithNarrativeNotes_StillPrintsTheHeadingAndTheNotes()
    {
        var analysis = SampleAnalysis.Of() with
        {
            PacingCurve = [new ChapterTension("1", 0.4)],
            PacingNotes = [new ReportFinding("Chapter 1", "Opens slow, picks up at the midpoint.")],
        };

        var text = TextOf(new AnalysisDocumentRenderer().Render(analysis, Cover, Array.Empty<SceneEvidence>()));

        Assert.Contains("Opens slow, picks up at the midpoint.", text, StringComparison.Ordinal);
    }

    private static byte[] Render(IReadOnlyList<ChapterTension> curve) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { PacingCurve = curve }, Cover, Array.Empty<SceneEvidence>());

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Concat(document.GetPages().Select(page => page.Text));
    }
}
