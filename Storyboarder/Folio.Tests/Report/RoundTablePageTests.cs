using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Reading;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;

namespace Folio.Tests.Report;

/// <summary>
/// The round table on the page (release H, R40–R41): a section and tab called Round table, in the verdict's
/// place — the transcript, speaker by name, then where the readers ended up. A report from before the round
/// table keeps its verdict.
/// </summary>
public class RoundTablePageTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 90_000, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private static readonly RoundTable Show = new RoundTable(
    [
        new RoundTableTurn(RoundTablePlan.HostId, "Welcome. Hugo loved it and Nick did not."),
        new RoundTableTurn("agent", "The opening sold me in a line."),
        new RoundTableTurn("harsh-critic", "And the middle lost me in a chapter.") { Answering = "agent" },
        new RoundTableTurn(RoundTablePlan.HostId, "Thank you both."),
    ])
    {
        Summary = new RoundTableSummary(["The opening works"], ["Whether the middle holds: Hugo yes, Nick no"]),
    };

    private static BookAnalysisResult Analysis(RoundTable? table) => SampleAnalysis.Of() with
    {
        Verdict = new Verdict(["a strength"], ["a problem"], "the middle compressed") { Standing = "The book is close." },
        RoundTable = table,
    };

    private static List<string> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text.Replace(" ", string.Empty, StringComparison.Ordinal))];
    }

    [Fact]
    public void TheRoundTablePrintsItsTranscriptThenWhereTheReadersEndedUpInTheVerdictsPlace()
    {
        var pages = Pages(new AnalysisDocumentRenderer().Render(Analysis(Show), Cover, []));
        var text = string.Concat(pages.SkipWhile(page => !page.Contains("Roundtable", StringComparison.Ordinal)));

        Assert.Contains("TaeBoll", text, StringComparison.Ordinal);
        Assert.Contains("HugoHook", text, StringComparison.Ordinal);
        Assert.Contains("Theopeningsoldmeinaline.", text, StringComparison.Ordinal);
        Assert.Contains("NickPicker", text, StringComparison.Ordinal);
        Assert.Contains("Wheretheyagree", text, StringComparison.Ordinal);
        Assert.Contains("Theopeningworks", text, StringComparison.Ordinal);
        Assert.Contains("Wheretheystilldiffer", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Thankyouboth.", StringComparison.Ordinal) < text.IndexOf("Wheretheyagree", StringComparison.Ordinal));
        Assert.DoesNotContain("Thebookisclose.", string.Concat(pages), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRoundTableHasItsOwnTabAndTheVerdictHasNone()
    {
        var pdf = new AnalysisDocumentRenderer().Render(Analysis(Show), Cover, []);

        using var document = PdfDocument.Open(new MemoryStream(pdf));
        var targets = document.GetPage(2).GetAnnotations().Select(annotation => annotation.Action)
            .OfType<AbstractGoToAction>().Count();
        var text = string.Concat(document.GetPages().Select(page => page.Text));
        Assert.True(targets > 0);
        Assert.Contains("Round table", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Biggest strengths", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportFromBeforeTheRoundTableKeepsItsVerdict()
    {
        var text = string.Concat(Pages(new AnalysisDocumentRenderer().Render(Analysis(null), Cover, [])));

        Assert.Contains("Thebookisclose.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TaeBoll", text, StringComparison.Ordinal);
    }
}
