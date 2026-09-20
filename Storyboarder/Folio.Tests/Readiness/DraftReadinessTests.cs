using Folio.Domain.Analysis;
using Folio.Domain.Readiness;

namespace Folio.Tests.Readiness;

/// <summary>
/// The readiness chart was built against a fixture — four tiers of counts, handed in. Nothing turned
/// a real draft into those counts, so the screen drew a shape that no book had ever produced. This
/// is that reading, and it is a Domain decision rather than a query because two of its four answers
/// are judgements rather than lookups.
/// </summary>
public class DraftReadinessTests
{
    [Fact]
    public void TheThreeActionTiersAreTheRevisionPlansOwn()
    {
        var reading = DraftReadiness.From(3, Report(
            plan:
            [
                Item("Must"), Item("Must"), Item("Must"),
                Item("Should"), Item("Should"),
                Item("Consider"),
            ]));

        Assert.Equal(3, reading.Counts[ReadinessTier.Must]);
        Assert.Equal(2, reading.Counts[ReadinessTier.Should]);
        Assert.Equal(1, reading.Counts[ReadinessTier.Consider]);
        Assert.Equal(3, reading.Number);
    }

    /// <summary>
    /// A tier the model spelled differently is still that tier. It writes the priority as free text,
    /// and a plan whose items all fall through to "other" would draw a book with no work to do.
    /// </summary>
    [Fact]
    public void ATierIsReadWhateverItsCaseAndPadding()
    {
        var reading = DraftReadiness.From(1, Report(plan: [Item(" must "), Item("SHOULD")]));

        Assert.Equal(1, reading.Counts[ReadinessTier.Must]);
        Assert.Equal(1, reading.Counts[ReadinessTier.Should]);
    }

    /// <summary>
    /// An unrecognised priority counts as Consider rather than as nothing. The item exists and the
    /// report prints it under "Other"; dropping it here would draw a draft with less to do than its
    /// own report lists.
    /// </summary>
    [Fact]
    public void AnUnrecognisedPriorityIsNotDiscarded()
    {
        var reading = DraftReadiness.From(1, Report(plan: [Item("Optional"), Item("")]));

        Assert.Equal(2, reading.Counts[ReadinessTier.Consider]);
    }

    /// <summary>
    /// Great is what the draft is doing well, and the verdict's strengths are where the report says
    /// so at book level. This is the tier that makes the chart a picture of improvement rather than
    /// of dwindling problems, so where it comes from is a decision, not a detail.
    /// </summary>
    [Fact]
    public void GreatIsTheVerdictsStrengths()
    {
        var reading = DraftReadiness.From(2, Report(
            plan: [Item("Must")],
            verdict: new Verdict(["The last five chapters.", "The withholding is a method."], [], "")));

        Assert.Equal(2, reading.Counts[ReadinessTier.Great]);
    }

    /// <summary>
    /// Every tier is present with a zero rather than absent. The chart drops a tier no draft carries,
    /// and a book whose Musts have all gone should show that line arriving at zero — not the line
    /// disappearing, which reads as the question no longer being asked.
    /// </summary>
    [Fact]
    public void EveryTierIsPresentEvenAtZero()
    {
        var reading = DraftReadiness.From(4, Report(plan: []));

        Assert.All(Enum.GetValues<ReadinessTier>(), tier => Assert.Equal(0, reading.Counts[tier]));
    }

    private static RevisionItem Item(string priority) =>
        new(priority, "Ch. 1", "Something to do", "A pointer");

    private static BookAnalysisResult Report(
        IReadOnlyList<RevisionItem> plan, Verdict? verdict = null)
    {
        var report = BookAnalysisResult.Empty with { RevisionPlan = plan };
        return verdict is null ? report : report with { Verdict = verdict };
    }
}
