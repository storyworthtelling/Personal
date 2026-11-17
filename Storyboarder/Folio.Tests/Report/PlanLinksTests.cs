using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Report;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;

namespace Folio.Tests.Report;

/// <summary>
/// Each revision-plan item links to the place its first finding is written, and has a box the tablet's
/// pen can tick (R15). The plan is the page carried to the desk; the link is how the author gets from an
/// item to the line it means without hunting for it.
/// </summary>
public class PlanLinksTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string One = "Chapter One\nRuth came back to Saltings in the spring.\n\n";
    private const string Two = "Chapter Two\nShe walked the sea wall for an hour and nobody spoke to her.";
    private const string Manuscript = One + Two;

    private static readonly IReadOnlyList<ChapterSpan> Chapters =
        [new(1, 0, One.Length), new(2, One.Length, Manuscript.Length)];

    private static readonly ReportFinding Quoted =
        new("Chapter 2", "Nobody reacts to her return.") { Excerpt = "nobody spoke to her" };

    private static readonly ReportFinding Unquoted = new("Chapter 1", "The return is unexplained.");

    [Fact]
    public void AnItemLinksToThePageItsFindingIsWrittenOn()
    {
        var key = Assert.Single(KeysOf(Quoted));
        using var pdf = PdfDocument.Open(Render(Item(key)));

        var target = PlanLinks(pdf).Single();
        // PdfPig runs a wrapped line into the next without a space, so the phrase is compared without them.
        Assert.Contains("nobodyspoketoher", pdf.GetPage(target).Text.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemWhoseFindingIsAChapterNoteLinksToThatChaptersList()
    {
        var key = Assert.Single(KeysOf(Unquoted));
        using var pdf = PdfDocument.Open(Render(Item(key)));

        var text = pdf.GetPage(PlanLinks(pdf).Single()).Text;
        Assert.Contains("Notes on this chapter", text, StringComparison.Ordinal);
        Assert.Contains("The return is unexplained.", text, StringComparison.Ordinal);
    }

    /// <summary>The page is also named, because a pen on paper cannot follow a link.</summary>
    [Fact]
    public void TheItemNamesThePageItLinksTo()
    {
        var key = Assert.Single(KeysOf(Quoted));
        using var pdf = PdfDocument.Open(Render(Item(key)));

        var plan = PlanPage(pdf);
        var named = System.Text.RegularExpressions.Regex.Match(plan.Text, @"page (\d+)");
        Assert.True(named.Success, "The item should say which page its finding is on.");
        var folio = named.Groups[1].Value;
        var target = pdf.GetPage(PlanLinks(pdf).Single());
        Assert.EndsWith(folio, target.GetWords().Last().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemThatDrawsOnNothingWrittenHasNoLink()
    {
        using var pdf = PdfDocument.Open(Render(Item("fdeadbeef")));

        Assert.Empty(PlanLinks(pdf));
    }

    /// <summary>
    /// The first finding written in reading order is where the author starts. Here both land on one sheet,
    /// so the test reads where on it each link lands: chapter one's list sits above chapter two's line.
    /// </summary>
    [Fact]
    public void AnItemDrawingOnSeveralLinksToTheEarliest()
    {
        var quoted = Assert.Single(KeysOf(Quoted));
        var both = KeysOf(Quoted, Unquoted);

        var alone = LandingOf(Render(Item(quoted)));
        var earliest = LandingOf(Render(Item([.. both])));

        Assert.Equal(alone.Page, earliest.Page);
        Assert.True(earliest.Top > alone.Top, "The link should land on chapter one’s list, above chapter two’s line.");
    }

    /// <summary>
    /// The box is big enough on the glass for a pen to tick: four millimetres, where the old nine-point box
    /// printed at about two.
    /// </summary>
    [Fact]
    public void TheBoxIsBigEnoughToTickOnTheTablet()
    {
        var millimetres = PageGeometry.PenBox * FolioSheet.Page.Scale * 25.4 / 72;

        Assert.True(millimetres >= 4.0, $"The box prints at {millimetres:F1} mm on the tablet.");
    }

    private static IReadOnlyList<string> KeysOf(params ReportFinding[] findings) =>
        [.. FindingCatalogue.Of(BookAnalysisResult.Empty with { PlotHoles = findings }).Select(entry => entry.Key)];

    private static RevisionItem Item(params string[] keys) =>
        new("Must", "Ch. 2", "Her return goes unremarked", "Let the village answer it.") { Draws = keys };

    private static byte[] Render(RevisionItem item) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { PlotHoles = [Quoted, Unquoted], RevisionPlan = [item] },
            Cover, [], review: null, Manuscript, Chapters);

    private static UglyToad.PdfPig.Content.Page PlanPage(PdfDocument pdf) =>
        pdf.GetPages().Single(page => page.Text.Contains("Her return goes unremarked", StringComparison.Ordinal));

    private static (int Page, double Top) LandingOf(byte[] report)
    {
        using var pdf = PdfDocument.Open(report);
        var plan = PlanPage(pdf);
        var item = plan.GetWords().First(word => word.Text == "Her");
        var link = plan.GetAnnotations().Single(annotation => annotation.Type == AnnotationType.Link
            && annotation.Rectangle.Left < item.BoundingBox.Right
            && annotation.Rectangle.Bottom <= item.BoundingBox.Top + 1
            && annotation.Rectangle.Top >= item.BoundingBox.Bottom - 1);
        var destination = Assert.IsAssignableFrom<AbstractGoToAction>(link.Action).Destination;
        return (destination.PageNumber, destination.Coordinates.Top ?? 0);
    }

    /// <summary>The links on the plan page other than the binder tabs (which lead to section starts).</summary>
    private static IReadOnlyList<int> PlanLinks(PdfDocument pdf)
    {
        var plan = PlanPage(pdf);
        var item = plan.GetWords().First(word => word.Text == "Her");
        return [.. plan.GetAnnotations()
            .Where(annotation => annotation.Type == AnnotationType.Link)
            .Where(annotation => annotation.Rectangle.Left < item.BoundingBox.Right
                && annotation.Rectangle.Bottom <= item.BoundingBox.Top + 1
                && annotation.Rectangle.Top >= item.BoundingBox.Bottom - 1)
            .Select(annotation => Assert.IsAssignableFrom<AbstractGoToAction>(annotation.Action).Destination.PageNumber)];
    }
}
