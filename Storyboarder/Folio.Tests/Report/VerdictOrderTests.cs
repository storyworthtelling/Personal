using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// G21 — the verdict prints strengths before problems.
///
/// <para>The bug this reintroduces is swapping them, which is a two-line edit that breaks nothing
/// and reads as an ordering preference. It is not one. A report whose first page of judgement is a
/// list of faults gets read defensively, and this document is asking its reader for a specific piece
/// of work rather than for agreement — so the order is a decision about whether the work gets done,
/// and it is invisible in any test that checks both lists are present.</para>
///
/// <para>Asserted by <b>position on the page</b> rather than by call order, because the thing that
/// matters is what a reader meets first. A renderer that printed them in the right order and then
/// laid them out in the wrong one would pass a call-order test and fail every author.</para>
/// </summary>
public class VerdictOrderTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private static readonly Verdict Judged = new(
        ["The last five chapters accelerate without leaving the close third."],
        ["The middle repeats itself across four chapters."],
        "One compression pass through the middle.");

    [Fact]
    public void StrengthsAreHigherOnThePageThanProblems()
    {
        var (strengths, problems) = Positions(Render(Judged));

        Assert.True(strengths is not null, "The verdict page never printed the strengths.");
        Assert.True(problems is not null, "The verdict page never printed the problems.");
        Assert.True(strengths < problems,
            $"Problems are printed above strengths (y {problems} above {strengths}). A report whose "
            + "first page of judgement is a list of faults gets read defensively.");
    }

    /// <summary>
    /// A draft with nothing wrong with it still opens with what is right. The order is not "good
    /// news first when there is any" — it is the shape of the page.
    /// </summary>
    [Fact]
    public void ADraftWithNoProblemsStillLeadsWithItsStrengths()
    {
        var (strengths, problems) = Positions(Render(Judged with { Problems = [] }));

        Assert.NotNull(strengths);
        Assert.Null(problems);
    }

    /// <summary>What the draft most needs closes the page, under both lists.</summary>
    [Fact]
    public void WhatTheDraftMostNeedsComesLast()
    {
        var text = Text(Render(Judged));

        var problems = text.IndexOf("BIGGEST PROBLEMS", StringComparison.OrdinalIgnoreCase);
        var needs = text.IndexOf("WHAT THIS DRAFT MOST NEEDS", StringComparison.OrdinalIgnoreCase);

        Assert.True(problems >= 0 && needs > problems,
            "The closing action has to arrive after the reading that argues for it.");
    }

    private static byte[] Render(Verdict verdict) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { Verdict = verdict }, Cover, Array.Empty<SceneEvidence>());

    // Where each label sits on its page, in PDF user space — where Y counts UP from the foot, so a
    // label higher on the page has the LARGER number. Returned inverted so "higher" reads as smaller.
    private static (double? Strengths, double? Problems) Positions(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));

        double? strengths = null;
        double? problems = null;
        foreach (var page in document.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                var after = page.Height - word.BoundingBox.Bottom;
                if (word.Text.Equals("STRENGTHS", StringComparison.OrdinalIgnoreCase))
                    strengths ??= after;
                if (word.Text.Equals("PROBLEMS", StringComparison.OrdinalIgnoreCase))
                    problems ??= after;
            }
        }

        return (strengths, problems);
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Concat(document.GetPages().Select(page => page.Text));
    }
}
