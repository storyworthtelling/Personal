using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Reading;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// The readers' contents page and the seven sheets it maps.
///
/// <para><b>The pointer is the whole of a contents page.</b> "p. 9" against the agent is a claim
/// about a sheet forty pages away, and nothing about the page it is printed on can be wrong in a way
/// that shows: the row looks right whatever number is on it. So the assertion here opens the
/// document and turns to the page the row names.</para>
///
/// <para>The orphan test is a regression, and it is here because no test found the bug — a render
/// did. The "Would need" block is pinned to the foot by a height the caller worked out, the first
/// version of that arithmetic forgot the label, and the last line fell past the foot: the wrapper
/// broke to a fresh sheet and every reader's page grew a nearly blank page behind it carrying half a
/// sentence. Seven of them, in a thirteen-page document.</para>
/// </summary>
public class ReaderPagesTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    [Fact]
    public void EveryReaderGetsAPageOfItsOwn()
    {
        var pages = PagesOf(Render(Reads()));

        foreach (var (persona, index) in PersonaCatalogue.Readers.Select((p, i) => (p, i + 1)))
        {
            Assert.True(
                pages.Any(page => page.Contains($"Reader {index} of seven", StringComparison.OrdinalIgnoreCase)
                    && page.Contains(persona.Name, StringComparison.Ordinal)),
                $"{persona.Name} has no page of its own. A whole-book read in character is not a "
                + "paragraph inside somebody else's section.");
        }
    }

    /// <summary>
    /// The page a contents row points at is the page that reader is on. Checked by turning to it,
    /// because a row that points at the wrong sheet looks exactly like a row that points at the
    /// right one.
    /// </summary>
    [Fact]
    public void EveryContentsRowPointsAtTheReadersOwnPage()
    {
        var pdf = Render(Reads());
        var pages = PagesOf(pdf);
        var folios = FoliosOf(pdf);
        var contents = pages.FindIndex(page => page.Contains("SEVEN READERS", StringComparison.OrdinalIgnoreCase));
        Assert.True(contents >= 0, "There is no readers' contents page.");

        // The folio printed on a sheet, and the index of that sheet in the file, differ by the front
        // matter — so the row's "p. N" is resolved through the folio the page itself carries.
        foreach (var (persona, index) in PersonaCatalogue.Readers.Select((p, i) => (p, i + 1)))
        {
            var pointer = Pointer(pages[contents], persona.Name);
            var turned = folios.IndexOf(pointer);

            Assert.True(turned >= 0, $"The contents sends the author to page {pointer}, which is not in the file.");
            Assert.Contains(persona.Name, pages[turned], StringComparison.Ordinal);
            Assert.Contains($"Reader {index} of seven", pages[turned], StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// No sheet of the readers' pages carries less than a sentence. A block pinned to the foot that
    /// overflows by a few points does not look like a layout a few points off — it looks like a
    /// report with blank pages in it.
    ///
    /// <para>Measured from the contents page onwards: the cover and the table of contents are
    /// deliberately sparse, and a rule that called them orphans would be a rule about the front
    /// matter rather than about the spill.</para>
    /// </summary>
    [Fact]
    public void NoPageIsAnOrphan()
    {
        var pages = PagesOf(Render(Reads()));
        var first = pages.FindIndex(page => page.Contains("SEVEN READERS", StringComparison.OrdinalIgnoreCase));
        Assert.True(first >= 0, "There is no readers' contents page.");

        var orphans = pages
            .Select((page, index) => (Page: page, Sheet: index + 1))
            .Skip(first)
            .Where(entry => entry.Page.Replace(Cover.Title, string.Empty).Trim().Length is > 0 and < 120)
            .Select(entry => $"sheet {entry.Sheet}: \"{entry.Page.Trim()}\"")
            .ToList();

        Assert.True(orphans.Count == 0,
            "A block pinned to the foot ran past it and spilled:\n" + string.Join("\n", orphans));
    }

    [Fact]
    public void AReportWithNoReadsHasNoReadersPages()
    {
        var pages = PagesOf(new AnalysisDocumentRenderer()
            .Render(SampleAnalysis.Of(), Cover, Array.Empty<SceneEvidence>()));

        Assert.DoesNotContain(pages, page => page.Contains("READERS", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] Render(IReadOnlyList<Review> reviews) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { Reviews = reviews }, Cover, Array.Empty<SceneEvidence>());

    private static IReadOnlyList<Review> Reads() =>
    [
        .. PersonaCatalogue.Readers.Select(persona => new Review(
            persona.Id,
            $"{persona.Name} arrives at the middle.",
            "A read long enough to be a read. It runs to several sentences because a whole-book "
            + "judgement in character is not a line, and the page is drawn around that length.\n\n"
            + "A second paragraph, so the split is exercised too.",
            [$"{persona.Name} found something that works.", "And a second thing."],
            [$"{persona.Name} found something that does not.", "And a second thing."],
            "Something specific would have to change before this reader changed their mind."))
    ];

    private static List<string> PagesOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }

    // The "p. N" printed against this reader on the contents page.
    private static int Pointer(string contents, string name)
    {
        var at = contents.IndexOf(name, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{name} has no row on the readers' contents page.");

        var marker = contents.IndexOf("p.", at, StringComparison.Ordinal);
        Assert.True(marker >= 0, $"{name}'s row names no page to turn to.");

        var after = contents[(marker + 2)..].TrimStart();
        return int.Parse(new string(after.TakeWhile(char.IsDigit).ToArray()));
    }

    /// <summary>
    /// The folio each sheet prints at its own foot — the number a reader would turn to.
    ///
    /// <para>Read off the page's <b>geometry</b>, as the lowest, right-most word. Taken off the end
    /// of the extracted text instead it comes back wrong and plausibly so: PdfPig joins runs without
    /// a separator, so the running foot "· draft 3" and the folio "5" arrive as "35".</para>
    /// </summary>
    private static List<int> FoliosOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));

        return [.. document.GetPages().Select(page =>
        {
            var foot = page.GetWords()
                .Where(word => word.BoundingBox.Bottom < 60)
                .OrderByDescending(word => word.BoundingBox.Left)
                .FirstOrDefault();
            return foot is not null && int.TryParse(foot.Text, out var folio) ? folio : -1;
        })];
    }
}
