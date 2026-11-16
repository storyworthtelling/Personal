using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Reading;
using Folio.Domain.Report;
using Folio.Domain.Text;
using ChapterSpan = Folio.Domain.Markup.ChapterSpan;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// Whole-book notes, leaner (R16): one short paragraph per section, every section's findings written on
/// the manuscript rather than listed under it, and a short story's notes on one page with only what is
/// true of the whole piece.
/// </summary>
public class LeanerNotesTests
{
    private const string Story =
        "The Last Ferry\nRuth came back to Saltings in the spring, and nobody spoke to her on the quay.\n\n"
        + "She walked the sea wall for an hour. The tide was out and the boats lay on their sides.";

    private static readonly IReadOnlyList<ChapterSpan> OneChapter = [new(1, 0, Story.Length)];

    [Fact]
    public void EverySectionsFindingsAreWrittenOnTheManuscript()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            SectionReads =
            [
                new SectionRead("story-integrity", "Holds together.",
                    [new ReportFinding("Chapter 1", "Nobody reacts to her return.") { Excerpt = "nobody spoke to her" }]),
                new SectionRead("theme-hierarchy", "Grief leads.", [new ReportFinding("Chapter 1", "The sea motif fades.")]),
            ],
        };

        var report = MarkSource.Build(Story, analysis, OneChapter);

        var mark = Assert.Single(report.Marks);
        Assert.Equal("Story integrity", mark.Label);
        var note = Assert.Single(Assert.Single(report.ChapterNotes).Notes);
        Assert.Equal("Theme hierarchy", note.Label);
    }

    /// <summary>
    /// A short story's note with no place of its own (no chapter cited, or an aggregate observation)
    /// goes to one list at the start of the story — it is about the whole piece — never to a list of
    /// its own in the whole-book notes.
    /// </summary>
    [Fact]
    public void AShortStorysUnplaceableNotesGatherAtItsStart()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            WordCount = 6_000,
            PlotHoles = [new ReportFinding(string.Empty, "Her return is never explained.")],
            DialogueNotes = [new DialogueNote(string.Empty, "Ruth, Ma", "They sound alike.", "f")],
        };

        var report = MarkSource.Build(Story, analysis, OneChapter);

        Assert.Empty(report.Unplaced);
        var list = Assert.Single(report.ChapterNotes);
        Assert.Equal("Notes on the story", list.Heading);
        Assert.Equal(2, list.Notes.Count);
    }

    [Fact]
    public void ANovelsChapterListKeepsItsHeading()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            WordCount = 90_000,
            PlotHoles = [new ReportFinding("Chapter 1", "Her return is never explained.")],
        };

        Assert.Equal("Notes on this chapter", Assert.Single(MarkSource.Build(Story, analysis, OneChapter).ChapterNotes).Heading);
    }

    [Theory]
    [InlineData(StoryLength.Novel, 4)]
    [InlineData(StoryLength.Novella, 4)]
    [InlineData(StoryLength.ShortStory, 2)]
    public void ASummaryIsCutToItsLengthsSentences(StoryLength length, int sentences)
    {
        const string Long = "One. Two? Three! Four. Five. Six.";

        var kept = WholeBookNotes.Summary(Long, length);

        Assert.Equal(sentences, kept.Count(c => c is '.' or '?' or '!'));
        Assert.StartsWith("One.", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortSummaryIsLeftAlone()
    {
        Assert.Equal("Holds together, mostly.", WholeBookNotes.Summary("Holds together, mostly.", StoryLength.ShortStory));
    }

    /// <summary>
    /// The page-count test the plan asked for: a short story whose every section read came back long, with
    /// findings, still prints its whole-book notes on one sheet.
    /// </summary>
    [Fact]
    public void AShortStorysWholeBookNotesFitOnOnePage()
    {
        const string Wordy =
            "The story holds its shape from the quay to the sea wall, and the return lands. The middle "
            + "repeats the walk without adding to it. The ending is quiet in a way that suits the piece. "
            + "A reader is never lost. The prose is plain and sure. The title earns its place.";
        var analysis = SampleAnalysis.Of() with
        {
            WordCount = 6_000,
            SectionReads = [.. PersonaCatalogue.Sections.Select(persona => new SectionRead(persona.Id, Wordy,
                [new ReportFinding(string.Empty, "Something about the whole piece that runs on for a while.")]))],
            VoiceConsistency = new VoiceConsistency("Plain, close third person, past tense, unhurried.", []),
        };

        var pdf = new AnalysisDocumentRenderer().Render(
            analysis,
            new ReportCover("The Last Ferry", 1, 6_000, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null),
            [], review: null, Story, OneChapter);

        // The notes run from their heading to the last section's paragraph; both on one sheet.
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        var pages = document.GetPages().ToList();
        var heading = pages.FindIndex(page =>
            page.Text.Replace(" ", string.Empty).Contains("Thebookisdoingasawhole", StringComparison.OrdinalIgnoreCase));
        // "quay to the sea wall" is in every section's paragraph and nowhere in the story itself.
        var first = pages.FindIndex(page => page.Text.Replace(" ", string.Empty).Contains("quaytotheseawall", StringComparison.Ordinal));

        var last = pages.FindLastIndex(page => page.Text.Replace(" ", string.Empty).Contains("quaytotheseawall", StringComparison.Ordinal));
        Assert.True(heading >= 0, "The notes should print.");
        Assert.Equal(heading, first);
        Assert.Equal(heading, last);
    }
}
