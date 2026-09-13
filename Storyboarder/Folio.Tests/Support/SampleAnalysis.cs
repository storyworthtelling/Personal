using Folio.Domain.Analysis;

namespace Folio.Tests.Support;

/// <summary>
/// A book analysis for the page tests. It exists so a test about the <i>page</i> does not have to
/// restate a sixteen-part record about the <i>report</i> — and so that adding a field to the report
/// does not send every page test red for a reason that has nothing to do with pages.
/// </summary>
public static class SampleAnalysis
{
    public static BookAnalysisResult Of(
        string logline = "A woman returns to the marsh village that drowned her brother.",
        string synopsis = "Ruth Vance comes back to Saltings after eleven years to settle her mother's estate.",
        IReadOnlyList<CharacterProfile>? characters = null,
        IReadOnlyList<ReportFinding>? plotHoles = null) =>
        new(
            logline,
            synopsis,
            characters ?? [],
            [],
            plotHoles ?? [],
            [],
            [],
            [],
            [],
            [],
            [],
            string.Empty,
            [],
            [],
            [],
            []);

    public static CharacterProfile Character(string name) => new(name, "narrator", "An arc.", []);
}
