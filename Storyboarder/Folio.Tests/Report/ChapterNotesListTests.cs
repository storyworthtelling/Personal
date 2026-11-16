using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// A note that found no passage prints in a list at its chapter's head (R7), under the chapter's title
/// and before its prose — not as a query in the margin beside the title.
/// </summary>
public class ChapterNotesListTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string One = "Chapter One\nRuth came back to Saltings in the spring.\n\n";
    private const string Two = "Chapter Two\nShe walked the sea wall for an hour and nobody spoke to her.";
    private const string Manuscript = One + Two;

    private static readonly IReadOnlyList<ChapterSpan> Chapters =
        [new(1, 0, One.Length), new(2, One.Length, Manuscript.Length)];

    [Fact]
    public void TheListSitsUnderTheChaptersTitleAndBeforeItsProse()
    {
        var text = TextOf(Render(SampleAnalysis.Of() with
        {
            PlotHoles = [new ReportFinding("Chapter 2", "Nobody asks where she has been.")],
        }));

        var title = text.IndexOf("Chapter Two", StringComparison.Ordinal);
        var list = text.IndexOf("Notes on this chapter", StringComparison.Ordinal);
        var note = text.IndexOf("Nobody asks where she has been.", StringComparison.Ordinal);
        var prose = text.IndexOf("She walked the sea wall", StringComparison.Ordinal);

        Assert.True(title >= 0 && title < list, "The list should follow the chapter's title.");
        Assert.True(list < note && note < prose, "The note should be in the list, before the chapter's prose.");
    }

    /// <summary>A book whose only notes are chapter notes still prints its manuscript, or they are lost.</summary>
    [Fact]
    public void ABookWithOnlyChapterNotesStillPrintsItsManuscript()
    {
        var text = TextOf(Render(SampleAnalysis.Of() with
        {
            LineEdits = [],
            PlotHoles = [new ReportFinding("Chapter 1", "The return is unexplained.")],
        }));

        Assert.Contains("Notes on this chapter", text, StringComparison.Ordinal);
        Assert.Contains("The return is unexplained.", text, StringComparison.Ordinal);
    }

    private static byte[] Render(BookAnalysisResult analysis) =>
        new AnalysisDocumentRenderer().Render(analysis, Cover, [], review: null, Manuscript, Chapters);

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Concat(document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
