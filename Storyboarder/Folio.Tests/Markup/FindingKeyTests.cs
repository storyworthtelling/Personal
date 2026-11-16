using Folio.Domain.Analysis;
using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// Every finding written on the manuscript carries a short key, so the revision plan can say which
/// findings each of its items draws on and the report can link the item to the place (R15).
///
/// <para>The key is the finding's own content, hashed, not a counter: the plan is written in one pass and
/// the report drawn in another, perhaps after a re-render, and both must name the same finding the same
/// way. A counter would move the moment one finding above it did.</para>
/// </summary>
public class FindingKeyTests
{
    private const string One = "Chapter One\nRuth came back to Saltings in the spring.\n\n";
    private const string Two = "Chapter Two\nShe walked the sea wall for an hour and nobody spoke to her.";
    private const string Manuscript = One + Two;

    private static readonly IReadOnlyList<ChapterSpan> Chapters =
        [new(1, 0, One.Length), new(2, One.Length, Manuscript.Length)];

    [Fact]
    public void TheSameFindingAlwaysHasTheSameKey()
    {
        Assert.Equal(
            FindingKey.Of("Plot holes", "Chapter 2", "nobody spoke", "Nobody reacts."),
            FindingKey.Of("Plot holes", "Chapter 2", "nobody spoke", "Nobody reacts."));
    }

    [Fact]
    public void ADifferentFindingHasADifferentKey()
    {
        Assert.NotEqual(
            FindingKey.Of("Plot holes", "Chapter 2", "nobody spoke", "Nobody reacts."),
            FindingKey.Of("Plot holes", "Chapter 2", "nobody spoke", "Everybody reacts."));
    }

    [Fact]
    public void AKeyIsShortEnoughToAskAModelToCopy()
    {
        Assert.Matches("^f[0-9a-f]{8}$", FindingKey.Of("Plot holes", "Chapter 2", "", "Nobody reacts."));
    }

    [Fact]
    public void AMarkAndAChapterNoteEachCarryTheirFindingsKey()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles =
            [
                new ReportFinding("Chapter 2", "Nobody reacts.") { Excerpt = "nobody spoke to her" },
                new ReportFinding("Chapter 1", "The return is unexplained."),
            ],
        };

        var report = MarkSource.Build(Manuscript, analysis, Chapters);
        var catalogue = FindingCatalogue.Of(analysis);

        var mark = Assert.Single(report.Marks);
        var note = Assert.Single(Assert.Single(report.ChapterNotes).Notes);
        Assert.Equal(2, catalogue.Count);
        Assert.Contains(catalogue, entry => entry.Key == mark.FindingKey && entry.Text.Contains("Nobody reacts."));
        Assert.Contains(catalogue, entry => entry.Key == note.FindingKey && entry.Text.Contains("The return is unexplained."));
    }

    /// <summary>
    /// The draft-over-draft tag ("[new]") is added when the report is drawn, long after the plan was
    /// written, so it must not move the key.
    /// </summary>
    [Fact]
    public void TheDraftStatusTagDoesNotMoveTheKey()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 2", "Nobody reacts.") { Excerpt = "nobody spoke to her" }],
        };

        var plain = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks);
        var tagged = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters, (_, _) => " [new]").Marks);

        Assert.EndsWith("[new]", tagged.Reason, StringComparison.Ordinal);
        Assert.Equal(plain.FindingKey, tagged.FindingKey);
    }

    /// <summary>The catalogue is what the plan's synthesis is shown, so it carries every kind of note.</summary>
    [Fact]
    public void TheCatalogueListsEveryKindOfFindingWithItsSeverity()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 2", "p") { Severity = "Must" }],
            LineEdits = [new LineEdit("Chapter 1", "Ruth came back", "x", "l")],
            ProseNotes = [new ProseNote("Chapter 1", "pattern", "Ruth came back", "f")],
            DialogueNotes = [new DialogueNote("Chapter 2", "s", "all sound alike", "f")],
            Expansion = new ExpansionReport(
                [new SceneAddition(AdditionKind.MissingBeat, "Chapter 2", "the funeral", "w", "Should")],
                [new ShowTellNote("Chapter 1", "Ruth came back", ShowTellDirection.Dramatize, "s")],
                LengthAssessment.Empty),
        };

        var catalogue = FindingCatalogue.Of(analysis);

        Assert.Equal(6, catalogue.Count);
        Assert.Equal(catalogue.Count, catalogue.Select(entry => entry.Key).Distinct().Count());
        Assert.Contains(catalogue, entry => entry.Label == "Plot hole" && entry.Severity == Folio.Domain.Readiness.ReadinessTier.Must);
        Assert.Contains(catalogue, entry => entry.Label == "Missing beat" && entry.Severity == Folio.Domain.Readiness.ReadinessTier.Should);
    }
}
