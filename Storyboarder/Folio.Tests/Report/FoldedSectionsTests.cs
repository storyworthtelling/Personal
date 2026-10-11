using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Reading;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// Twelve sections stopped being pages and became marks on the manuscript. What these pin is the
/// two ways that move can go wrong without anything failing: a section quietly coming back, and a
/// finding quietly disappearing because the section that listed it is gone.
/// </summary>
public class FoldedSectionsTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string One = "Ruth came back to Saltings in the spring.\nThe water had taken her brother.\n\n";
    private const string Two = "Chapter Two\nShe walked the sea wall for an hour, and nobody spoke to her.\n\n";
    private const string Manuscript = One + Two;

    private static readonly IReadOnlyList<ChapterSpan> Chapters =
        [new(1, 0, One.Length), new(2, One.Length, Manuscript.Length)];

    /// <summary>Headings that were sections of their own. None may print as one again.</summary>
    private static readonly string[] RetiredHeadings =
    [
        "Plot holes", "Inconsistencies", "Inaccuracies", "Prose style & voice", "Line-level edits",
        "Show vs tell", "Authorial voice consistency", "Scenes to add", "Scene by scene",
    ];

    private static BookAnalysisResult Everything() => SampleAnalysis.Of(plotHoles:
        [new ReportFinding("Chapter 2", "Nobody reacts to her return.") { Excerpt = "nobody spoke to her" }]) with
    {
        Inconsistencies = [new ReportFinding("Chapter 1", "Her brother is two ages at once.")],
        LineEdits = [new LineEdit("Chapter 1", "Ruth came back to Saltings", "x", "A flat opening.")],
        Expansion = new ExpansionReport(
            [new SceneAddition(AdditionKind.MissingBeat, "after Chapter 1", "The funeral.", "It is skipped.", "Must")],
            [new ShowTellNote("Chapter 2", "She walked the sea wall for an hour", ShowTellDirection.Tighten, "Runs long.")],
            LengthAssessment.Empty),
        PacingCurve = [new ChapterTension("1", 0.3), new ChapterTension("2", 0.8)],
    };

    [Fact]
    public void NoneOfTheRetiredSectionsPrintsAsASection()
    {
        var text = TextOf(Render(Everything()));

        foreach (var heading in RetiredHeadings)
            Assert.DoesNotContain(heading, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFindingsAreWrittenOnTheManuscriptNotListedBeforeIt()
    {
        var pages = PagesOf(Render(Everything()));
        var marked = pages.Single(page => page.Contains("reacts", StringComparison.Ordinal));

        // On a page of the manuscript, beside the sentence it concerns.
        Assert.Contains("The water had taken her brother.", marked, StringComparison.Ordinal);
        Assert.Contains("Plot hole", marked, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryKindOfNoteReachesAPage()
    {
        var text = TextOf(Render(Everything()));

        // The margin is narrow, so a note wraps and interleaves with the prose beside it in the
        // extracted text; one distinctive word per note is what can be matched reliably.
        Assert.Contains("ages", text, StringComparison.Ordinal);      // finding placed by chapter
        Assert.Contains("flat", text, StringComparison.Ordinal);      // line edit
        Assert.Contains("funeral", text, StringComparison.Ordinal);   // scene to add
        Assert.Contains("Runs", text, StringComparison.Ordinal);      // show vs tell
    }

    /// <summary>The pacing diagram is about the whole book, so it moved to the whole-book notes with it.</summary>
    [Fact]
    public void ThePacingChartPrintsInTheWholeBookNotes()
    {
        var pages = PagesOf(Render(Everything()));
        var chart = pages.Single(page => page.Contains("Tension across the chapters", StringComparison.Ordinal));

        Assert.Contains("Whole-book notes", chart, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDominantVoiceAndTheCarriedThreadsAreWholeBookNotes()
    {
        var analysis = Everything() with
        {
            VoiceConsistency = new VoiceConsistency("Dry and spare.", []),
            SeriesContinuity = new SeriesContinuity("Salt", [], [new CarriedThread("Who lit the lamp", "Ignored")]),
        };

        var text = TextOf(Render(analysis));

        Assert.Contains("Dry and spare.", text, StringComparison.Ordinal);
        Assert.Contains("Who lit the lamp", text, StringComparison.Ordinal);
    }

    // ---- nothing is lost -------------------------------------------------------------------------

    /// <summary>
    /// The reason the unplaced list exists. Rendered with no manuscript there is nothing to write on,
    /// and the report must still say everything it found.
    /// </summary>
    [Fact]
    public void WithNoManuscriptEveryFindingIsStillPrinted()
    {
        var text = TextOf(new AnalysisDocumentRenderer().Render(Everything(), Cover, Array.Empty<SceneEvidence>()));

        Assert.Contains("Not tied to a place in the text", text, StringComparison.Ordinal);
        Assert.Contains("Nobody reacts to her return.", text, StringComparison.Ordinal);
        Assert.Contains("Her brother is two ages at once.", text, StringComparison.Ordinal);
        Assert.Contains("A flat opening.", text, StringComparison.Ordinal);
        Assert.Contains("The funeral.", text, StringComparison.Ordinal);
        Assert.Contains("Runs long.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenEverythingIsPlacedNoUnplacedHeadingPrints()
    {
        var text = TextOf(Render(Everything()));

        Assert.DoesNotContain("Not tied to a place in the text", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAggregateObservationIsPrintedAmongTheWholeBookNotes()
    {
        var analysis = SampleAnalysis.Of() with
        {
            DialogueNotes = [new DialogueNote("Chapter 2", "Ruth, Anna", "All three sound identical.", "Vary the register.")],
            LineEdits = [new LineEdit("Chapter 1", "Ruth came back to Saltings", "x", "A flat opening.")],
        };

        var pages = PagesOf(Render(analysis));
        var notes = pages.Single(page => page.Contains("All three sound identical.", StringComparison.Ordinal));

        Assert.Contains("Whole-book notes", notes, StringComparison.Ordinal);
    }

    /// <summary>A sequel's carried threads stay quiet for a standalone book, as the series section always did.</summary>
    [Fact]
    public void AStandaloneBooksSeriesPersonaStaysSilent()
    {
        var analysis = SampleAnalysis.Of() with
        {
            SectionReads = [new SectionRead("series-continuity", "No prior book was provided.", [])],
        };

        Assert.DoesNotContain("No prior book was provided.", TextOf(Render(analysis)), StringComparison.Ordinal);
    }

    private static byte[] Render(BookAnalysisResult analysis) =>
        new AnalysisDocumentRenderer().Render(
            analysis, Cover, Array.Empty<SceneEvidence>(), review: null, Manuscript, Chapters);

    private static List<string> PagesOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }

    private static string TextOf(byte[] pdf) => string.Concat(PagesOf(pdf));
}
