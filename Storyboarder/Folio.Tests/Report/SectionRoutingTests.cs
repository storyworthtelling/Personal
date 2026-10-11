using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Reading;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// Where each whole-book section persona's own read ends up in the printed report.
///
/// <para>The bug this reintroduces: the eight section personas' one-line summaries were dumped
/// wholesale into Themes, overwriting the model's actual thematic answer with eight unlabeled
/// sentences about structure, pacing, and length — found on the first real report, whose Themes
/// bullets had nothing to do with themes. Three of the eight sections (Pacing, Voice consistency,
/// Series continuity) already have a home elsewhere in the report and fold into it; the rest print
/// under their own heading, named by persona.</para>
/// </summary>
public class SectionRoutingTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    [Fact]
    public void ThemesStaysTheModelsOwnAnswer_NotASectionSummary()
    {
        var analysis = SampleAnalysis.Of() with
        {
            Themes = ["perseverance against the marsh"],
            SectionReads = [new SectionRead("pacing", "The middle slackens for two chapters.", [])],
        };

        var text = TextOf(Render(analysis));
        var themes = text.IndexOf("Themes", StringComparison.Ordinal);
        var pacingPersona = text.IndexOf("The middle slackens", StringComparison.Ordinal);

        Assert.Contains("perseverance against the marsh", text, StringComparison.Ordinal);
        // The persona summary prints under its own Pacing heading, strictly after Themes closes —
        // not folded into the Themes bullets it used to overwrite.
        Assert.True(themes >= 0 && pacingPersona > themes,
            "The pacing persona's summary should print after Themes, not inside it.");
    }

    [Fact]
    public void ThePacingPersonasSummaryFoldsIntoThePacingSection()
    {
        var analysis = SampleAnalysis.Of() with
        {
            SectionReads = [new SectionRead("pacing", "The middle slackens for two chapters.", [])],
        };

        var pages = PagesOf(Render(analysis));
        var pacingPage = pages.Single(page => page.Contains("The middle slackens", StringComparison.Ordinal));

        Assert.Contains("Pacing", pacingPage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVoiceConsistencyPersonasSummaryFoldsIntoThatSection()
    {
        var analysis = SampleAnalysis.Of() with
        {
            VoiceConsistency = new VoiceConsistency("A dry, self-aware voice.", []),
            SectionReads = [new SectionRead("voice-consistency", "Slips twice into a composed register.", [])],
        };

        var pages = PagesOf(Render(analysis));
        var voicePage = pages.Single(page => page.Contains("Slips twice into a composed register.", StringComparison.Ordinal));

        // Under the whole-book notes now, as "Voice consistency"; the old standalone section is gone.
        Assert.Contains("Voice consistency", voicePage, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorial voice consistency", voicePage, StringComparison.Ordinal);
    }

    /// <summary>Quiet by default: a standalone book's "no prior book" note isn't worth a page.</summary>
    [Fact]
    public void TheSeriesContinuityPersonaStaysSilentForAStandaloneBook()
    {
        var analysis = SampleAnalysis.Of() with
        {
            SeriesContinuity = SeriesContinuity.Empty,
            SectionReads = [new SectionRead("series-continuity", "No prior book was provided.", [])],
        };

        var text = TextOf(Render(analysis));

        Assert.DoesNotContain("No prior book was provided.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Series continuity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSeriesContinuityPersonaSummaryPrintsWhenTheSectionAlreadyHasContent()
    {
        var analysis = SampleAnalysis.Of() with
        {
            SeriesContinuity = new SeriesContinuity(
                "Prior Book", [new SeriesBreak("Ch. 1", "Established", "Contradicted")], []),
            SectionReads = [new SectionRead("series-continuity", "Mostly holds together.", [])],
        };

        var pages = PagesOf(Render(analysis));
        var seriesPage = pages.Single(page => page.Contains("Mostly holds together.", StringComparison.Ordinal));

        Assert.Contains("Series continuity", seriesPage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The five sections with no existing home — Story integrity, Structure, Setup and payoff,
    /// Theme hierarchy, Length — print under their own heading, each labelled by persona so the
    /// reader knows which voice said what (the flattened Themes bullets never said).
    /// </summary>
    [Fact]
    public void TheUnhomedSectionsPrintUnderTheirOwnHeading_NamedByPersona()
    {
        var analysis = SampleAnalysis.Of() with
        {
            SectionReads =
            [
                new SectionRead("structure", "The middle sags.", [new ReportFinding("Ch. 5", "No act break lands here.")]),
                new SectionRead("length", "Short of the genre norm.", []),
            ],
        };

        var pages = PagesOf(Render(analysis));
        var notesPage = pages.Single(page => page.Contains("No act break lands here.", StringComparison.Ordinal));

        Assert.Contains("Whole-book notes", notesPage, StringComparison.Ordinal);
        Assert.Contains("Structure", notesPage, StringComparison.Ordinal);
        Assert.Contains("The middle sags.", notesPage, StringComparison.Ordinal);
        Assert.Contains("Length", notesPage, StringComparison.Ordinal);
        Assert.Contains("Short of the genre norm.", notesPage, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSectionReadsAtAll_PrintsNoWholeBookNotesSection()
    {
        var text = TextOf(Render(SampleAnalysis.Of()));

        Assert.DoesNotContain("Whole-book notes", text, StringComparison.Ordinal);
    }

    private static byte[] Render(BookAnalysisResult analysis) =>
        new AnalysisDocumentRenderer().Render(analysis, Cover, Array.Empty<SceneEvidence>());

    private static List<string> PagesOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }

    private static string TextOf(byte[] pdf) => string.Concat(PagesOf(pdf));
}
