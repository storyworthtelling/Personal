using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// F42 — the revision plan spills overleaf when it is long, and the tier heading repeats.
///
/// <para>The bug this reintroduces is printing the heading once. It is invisible on any plan short
/// enough to fit a sheet, which is every plan a test writes by hand and most of the ones a report
/// produces — so it was shipped and only a render of a long one found it. The continuation page
/// opened with unheaded items whose <b>only</b> tier cue was the severity chip. That is colour
/// alone, and DESIGN.md §16's non-colour-cue rule is the one thing a printed page may not lean on:
/// the page that gets carried to the desk would have been asking the author to tell ochre from
/// slate to know which items are optional.</para>
/// </summary>
public class PlanContinuationTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    [Fact]
    public void ATierThatSpillsOverleafRepeatsItsHeading()
    {
        var pages = Render(Plan(musts: 2, shoulds: 22, considers: 2));

        var carried = pages.Where(page => page.Contains("(continued)", StringComparison.Ordinal)).ToList();

        Assert.True(carried.Count > 0,
            "A plan of twenty-six items did not spill, so this test is no longer testing anything. "
            + "Lengthen it until it does.");
        Assert.Contains(carried, page => page.Contains("Should (continued)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The repeat says "continued", not the bare tier. A second heading reading just "Should" is a
    /// second list of the same name, and an author counting Shoulds would count some of them twice.
    ///
    /// <para>A tier long enough spills over more than one break, and each carried page gets its own
    /// repeat — so the assertion is that the tier heads its list <b>once</b> and every appearance
    /// after that is marked as a continuation. (The tally at the top of the page prints the tier as
    /// a letterspaced caps label, which is a different string.)</para>
    /// </summary>
    [Fact]
    public void TheRepeatSaysItIsARepeat()
    {
        var all = string.Concat(Render(Plan(musts: 2, shoulds: 22, considers: 2)));

        Assert.True(Occurrences(all, "Should (continued)") >= 1);
        Assert.Equal(1, Occurrences(all, "Should") - Occurrences(all, "Should (continued)"));
    }

    /// <summary>
    /// A plan that fits one sheet says "continued" nowhere. The repeat is a consequence of the
    /// break, not a decoration on the heading.
    /// </summary>
    [Fact]
    public void APlanThatFitsSaysNothingAboutContinuing()
    {
        var pages = Render(Plan(musts: 2, shoulds: 2, considers: 2));

        Assert.DoesNotContain(pages, page => page.Contains("(continued)", StringComparison.Ordinal));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static IReadOnlyList<RevisionItem> Plan(int musts, int shoulds, int considers) =>
    [
        .. Items("Must", musts), .. Items("Should", shoulds), .. Items("Consider", considers),
    ];

    private static IEnumerable<RevisionItem> Items(string tier, int count) =>
        Enumerable.Range(1, count).Select(i => new RevisionItem(
            tier, $"Ch. {i}",
            $"A {tier.ToLowerInvariant()} item, number {i}, written at the length these actually run to.",
            "And the pointer under it, which explains why it matters without writing the line."));

    private static List<string> Render(IReadOnlyList<RevisionItem> plan)
    {
        var pdf = new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { RevisionPlan = plan }, Cover, Array.Empty<SceneEvidence>());

        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }
}
