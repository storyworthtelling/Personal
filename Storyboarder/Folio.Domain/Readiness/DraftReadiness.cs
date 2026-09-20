using Folio.Domain.Analysis;

namespace Folio.Domain.Readiness;

/// <summary>
/// One draft's report, read as four counts.
///
/// <para>The chart was built against a hand-written fixture and nothing produced one from a real
/// book, so the screen drew a shape no draft had ever made. This is the missing half, and it lives
/// in Domain rather than in a query because two of the four answers are judgements: what an
/// unrecognised priority counts as, and where <see cref="ReadinessTier.Great"/> comes from at all.
/// </para>
///
/// <para><b>Great is the verdict's strengths.</b> The revision plan has three tiers and they are all
/// work to do; a chart of only those is a picture of a book's problems shrinking, which is not the
/// same claim as a book getting better. The verdict is where the report says at book level what is
/// working, so that count is the fourth line — and it is the one that should rise.</para>
/// </summary>
public static class DraftReadiness
{
    public static DraftFindings From(int draftNumber, BookAnalysisResult report)
    {
        var counts = Enum.GetValues<ReadinessTier>().ToDictionary(tier => tier, _ => 0);

        foreach (var item in report.RevisionPlan ?? [])
        {
            counts[Tier(item.Priority)]++;
        }

        counts[ReadinessTier.Great] = report.Verdict.Strengths?.Count ?? 0;

        return new DraftFindings(draftNumber, counts);
    }

    /// <summary>
    /// The model writes a priority as free text, so this reads one rather than parsing it. Anything
    /// it does not recognise is a Consider: the item exists, the report prints it, and a plan whose
    /// wording drifted would otherwise draw a draft with less to do than its own pages list.
    /// </summary>
    private static ReadinessTier Tier(string? priority) =>
        (priority ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "must" => ReadinessTier.Must,
            "should" => ReadinessTier.Should,
            _ => ReadinessTier.Consider,
        };
}
