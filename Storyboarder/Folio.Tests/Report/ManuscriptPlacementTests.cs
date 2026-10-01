using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Reading;
using Folio.Domain.Shelf;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// Where the marked-up manuscript sits relative to the readers.
///
/// <para>The manuscript is what P10 rebuilt the whole document around: "anything anchored to a
/// place in the text lives on the text; anything about the book as a whole stays a section" — and
/// the report anatomy that rule produced puts the manuscript right after the whole-book sections,
/// before the readers ever open their mouths. The renderer had it backwards: readers, then the
/// scene walk, then the manuscript last of all — the centre of the report printing closer to the
/// back cover than the frame. Nothing caught it because every other placement test checks a page's
/// own internals, not where the page sits among its neighbours.</para>
///
/// <para>The heading alone is not enough to find the real page by: the front contents page names
/// "The manuscript, marked" as a row too, and a row pointing at page 29 is not page 29. So the
/// search requires the heading <b>and</b> a line of the manuscript's own prose on the same sheet —
/// true only where the pages actually are.</para>
/// </summary>
public class ManuscriptPlacementTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string Manuscript =
        "Ruth stood at the sea wall and did not look back at the house.\n\n"
        + "The tide was already turning, the way it always did at this hour.";

    // Unique to the real heading — the front contents page prints the row title alone, with no
    // caption underneath it. The manuscript's own prose starts on the page after this one:
    // ManuscriptPage forces a fresh sheet before a single line of it is drawn.
    private const string HeadingCaption = "None of them writes the line for you.";

    [Fact]
    public void TheMarkedManuscriptComesBeforeTheReaders()
    {
        var pages = PagesOf(Render());

        var manuscript = pages.FindIndex(page =>
            page.Contains("The manuscript, marked", StringComparison.Ordinal)
            && page.Contains(HeadingCaption, StringComparison.Ordinal));
        var readers = pages.FindIndex(page => page.Contains("READERS", StringComparison.OrdinalIgnoreCase)
            && page.Contains(BookLines.Spell(PersonaCatalogue.Readers.Count), StringComparison.OrdinalIgnoreCase));

        Assert.True(manuscript >= 0, "The manuscript's own page never printed — check the fixture's excerpt still matches.");
        Assert.True(readers >= 0, "The readers' contents page never printed.");
        Assert.True(manuscript < readers,
            $"The manuscript's own page is page {manuscript + 1} and the readers start on page "
            + $"{readers + 1}: the centre of the report is printing after the voices that read it, "
            + "not before.");
    }

    private static byte[] Render() =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with
            {
                LineEdits = [new LineEdit(
                    "Chapter 1",
                    "did not look back at the house",
                    "Cut the negation; show her walking away instead.",
                    "Telling us what she didn't do is weaker than the action itself.")],
                Reviews = Reads(),
            },
            Cover, Array.Empty<SceneEvidence>(), manuscript: Manuscript);

    private static IReadOnlyList<Review> Reads() =>
    [
        .. PersonaCatalogue.Readers.Select(persona => new Review(
            persona.Id,
            $"{persona.Name} arrives at the middle.",
            "A read long enough to be a read.",
            [$"{persona.Name} found something that works."],
            [$"{persona.Name} found something that does not."],
            "Something specific would have to change before this reader changed their mind."))
    ];

    private static List<string> PagesOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }
}
