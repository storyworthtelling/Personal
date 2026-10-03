using Index.Domain.External;

namespace Index.Domain.Model;

/// <summary>How often a detected income stream lands (D101). Semimonthly (twice a month, on fixed
/// calendar days — the 1st and 15th, or the 15th and month's end) is deliberately its own cadence
/// rather than folded into Biweekly: both average roughly 15 days apart, but a biweekly paycheck
/// repeats every exactly-14-days and so falls on the same weekday every time, while semimonthly pay
/// dates drift across weekdays because they are calendar-day-anchored, not interval-anchored. That
/// weekday-drift is exactly what <see cref="IncomeDetector"/> uses to tell the two apart.</summary>
public enum IncomeCadence
{
    Weekly,
    Biweekly,
    Semimonthly,
    Monthly,
}

/// <summary>One recurring deposit pattern <see cref="IncomeDetector"/> found — a single paycheck or
/// side-income source, never the household's whole income (there can be several; see
/// <see cref="IncomeEstimate.Streams"/>).</summary>
public sealed record IncomeStream(
    IncomeCadence Cadence,
    decimal TypicalAmount,
    int OccurrenceCount,
    decimal ProjectedMonthlyContribution,
    DateTimeOffset FirstObserved,
    DateTimeOffset LastObserved);

/// <summary>
/// D101–D104's result: what <see cref="IncomeDetector"/> could confidently work out about household
/// income from recurring Plaid deposits, or why it couldn't. <see cref="ProjectedMonthlyIncome"/> is
/// null exactly when <see cref="IsConfident"/> is false — D103's "not sure," never a guessed number.
/// </summary>
public sealed record IncomeEstimate(
    decimal? ProjectedMonthlyIncome,
    bool IsConfident,
    string? Reason,
    IReadOnlyList<IncomeStream> Streams)
{
    public static IncomeEstimate NotConfident(string reason) => new(null, false, reason, []);
}

/// <summary>
/// D101–D104: infers household income from recurring Plaid deposits, never from a manually entered
/// figure. Pure — takes whatever transaction history it's handed and returns a verdict, with no I/O
/// and no notion of "the current month" (that absence is structural, not an oversight: D102's whole
/// point is that a projected monthly figure must not depend on which calendar month happened to be
/// sampled, so this type is never given one to depend on). <see cref="IncomeDetectionService"/> is the
/// Infrastructure half that fetches the transactions this reads.
///
/// <para><b>D104's scoping is structural, not enforced here.</b> This detector only ever sees whatever
/// <see cref="IncomeDetectionService"/> passes it, which is only ever this install's own
/// Plaid-connected accounts' transactions (the same set <c>AccountService</c>/<c>BudgetService</c>
/// already scope to) — an account nobody connected never produces a transaction for this type to see,
/// so there is nothing for the algorithm itself to filter.</para>
///
/// <para><b>Sign convention</b> (confirmed against <see cref="PlaidTransaction"/>'s own doc comment and
/// <c>BudgetService.RefreshTransactionsAsync</c>'s cast): Plaid signs a transaction positive for money
/// LEAVING the account and negative for money coming IN. A deposit candidate is therefore
/// <c>Amount &lt; 0</c>; its income-facing size is <c>-Amount</c>.</para>
///
/// <para><b>The exact thresholds, and why:</b></para>
/// <list type="bullet">
/// <item><description><c>AmountToleranceFraction = 0.08</c> (8%). A paycheck varies a little from cycle
/// to cycle — overtime, a tax-withholding change, a small raise not yet reflected everywhere — and 8%
/// comfortably covers that ordinary drift ($2,400 ± $192) while still rejecting a deposit that is
/// merely coincidentally close in size to a real paycheck cluster (a $2,550 one-off next to a $2,400
/// stream is 6.25% off — inside a looser band, outside this one only once the two are genuinely
/// different amounts; the tests below exercise the boundary). Applied against the CLUSTER's own running
/// average, not the first deposit seen, so early noise doesn't lock in a bad reference point.</description></item>
/// <item><description><c>MinOccurrencesForConfidence = 3</c>. Two deposits give exactly one interval —
/// a single gap can look "regular" by pure chance, and there is nothing to compare it against. Three
/// deposits give two independently-measured intervals, which is the minimum needed to say the SPACING
/// itself is stable rather than a coincidence. This is also what makes "only one observed paycheck"
/// resolve to "not sure" for free: a cluster of size one never reaches this floor, so it is simply
/// never proposed as a stream — no special case needed.</description></item>
/// <item><description>Per-cadence interval tolerance, in days, around each cadence's nominal spacing —
/// wide enough to absorb a weekend/holiday shifting a payday by a day or two, narrow enough that a
/// genuinely different cadence (or no cadence at all) doesn't get rounded into the nearest one:
/// Weekly 7±2, Biweekly 14±3, Semimonthly avg 15.2 with each interval inside [12,19], Monthly avg 30.4
/// with each interval inside [25,35].</description></item>
/// <item><description>Biweekly vs. semimonthly: a cluster whose intervals sit inside the biweekly band
/// AND whose deposits fall on the same weekday every time is Biweekly; one whose intervals sit inside
/// the wider semimonthly band but do NOT hold a single weekday is Semimonthly. Interval-only
/// classification can't tell these apart (both average ~14–16 days) — the weekday check is what
/// separates "every 14 days" from "the 1st and the 15th."</description></item>
/// </list>
///
/// <para><b>Not sure, deliberately:</b> a deposit that lands in no cadence-fitting, amount-tolerant
/// cluster of at least three simply never becomes a stream — it is excluded from income entirely
/// (D101's "not a one-off transfer or refund"), not counted at some discounted confidence. The overall
/// estimate is confident only when at least one stream qualifies; when household deposits exist but
/// none qualifies (irregular gig income, self-employment, too little history), the result is
/// <see cref="IncomeEstimate.NotConfident"/> with a stated reason, never a guessed number (D103).</para>
/// </summary>
public static class IncomeDetector
{
    public const decimal AmountToleranceFraction = 0.08m;
    public const int MinOccurrencesForConfidence = 3;

    private const int WeeklyNominalDays = 7;
    private const int WeeklyToleranceDays = 2;

    private const int BiweeklyNominalDays = 14;
    private const int BiweeklyToleranceDays = 3;

    private const int SemimonthlyMinDays = 12;
    private const int SemimonthlyMaxDays = 19;

    private const int MonthlyMinDays = 25;
    private const int MonthlyMaxDays = 35;

    public static IncomeEstimate Detect(IReadOnlyList<PlaidTransaction> transactions)
    {
        var deposits = transactions
            .Where(t => t.Amount < 0)
            .Select(t => (Date: t.Date, Amount: -(decimal)t.Amount))
            .OrderBy(d => d.Date)
            .ToList();

        if (deposits.Count == 0)
        {
            return IncomeEstimate.NotConfident("No deposits were found on any connected account.");
        }

        var clusters = ClusterByAmount(deposits);
        var streams = new List<IncomeStream>();

        foreach (var cluster in clusters)
        {
            if (TryBuildStream(cluster, out var stream))
            {
                streams.Add(stream);
            }
        }

        if (streams.Count == 0)
        {
            return IncomeEstimate.NotConfident(
                "Deposits don’t repeat on a steady schedule, so income can’t be inferred with confidence.");
        }

        var projected = streams.Sum(s => s.ProjectedMonthlyContribution);
        return new IncomeEstimate(projected, IsConfident: true, Reason: null, streams);
    }

    // Groups deposits by amount similarity: each deposit joins the first existing cluster whose
    // running average it falls within AmountToleranceFraction of, else starts a new cluster. A running
    // average (not the cluster's first member) keeps the reference point from drifting badly on a slow
    // trend (a gradual raise) while still rejecting an unrelated amount outright.
    private static List<List<(DateTimeOffset Date, decimal Amount)>> ClusterByAmount(
        List<(DateTimeOffset Date, decimal Amount)> deposits)
    {
        var clusters = new List<List<(DateTimeOffset Date, decimal Amount)>>();

        foreach (var deposit in deposits)
        {
            var home = clusters.FirstOrDefault(cluster =>
            {
                var average = cluster.Average(d => d.Amount);
                return Math.Abs(deposit.Amount - average) <= average * AmountToleranceFraction;
            });

            if (home is not null)
            {
                home.Add(deposit);
            }
            else
            {
                clusters.Add([deposit]);
            }
        }

        return clusters;
    }

    private static bool TryBuildStream(
        List<(DateTimeOffset Date, decimal Amount)> cluster, out IncomeStream stream)
    {
        stream = null!;

        if (cluster.Count < MinOccurrencesForConfidence)
        {
            return false; // one interval, or none, is never enough to call a pattern stable
        }

        var intervals = new List<double>();
        for (var i = 1; i < cluster.Count; i++)
        {
            intervals.Add((cluster[i].Date - cluster[i - 1].Date).TotalDays);
        }

        var cadence = ClassifyCadence(cluster, intervals);
        if (cadence is null)
        {
            return false; // amounts matched, but the spacing fits no cadence — not a recurring stream
        }

        var typicalAmount = cluster.Average(d => d.Amount);

        // Multiply by the occurrences-per-year numerator BEFORE dividing by 12, rather than
        // pre-computing a "52/12" or "26/12" ratio as its own decimal: 26/12 has no exact decimal
        // representation, so rounding it first (even to decimal's ~28-digit precision) and THEN
        // multiplying by the typical amount leaves a sub-cent residue (e.g. $5,200.0000000000000000000000001)
        // that a whole-dollar-rounding worksheet composer downstream must never have to absorb.
        var (perYearOccurrences, monthsPerYear) = cadence.Value switch
        {
            IncomeCadence.Weekly => (52m, 12m),
            IncomeCadence.Biweekly => (26m, 12m),
            IncomeCadence.Semimonthly => (24m, 12m), // 2/month × 12
            IncomeCadence.Monthly => (12m, 12m),
            _ => throw new ArgumentOutOfRangeException(nameof(cadence)),
        };
        var projectedMonthlyContribution = typicalAmount * perYearOccurrences / monthsPerYear;

        stream = new IncomeStream(
            cadence.Value,
            typicalAmount,
            cluster.Count,
            projectedMonthlyContribution,
            cluster[0].Date,
            cluster[^1].Date);
        return true;
    }

    private static IncomeCadence? ClassifyCadence(
        List<(DateTimeOffset Date, decimal Amount)> cluster, List<double> intervals)
    {
        if (intervals.All(i => Math.Abs(i - WeeklyNominalDays) <= WeeklyToleranceDays))
        {
            return IncomeCadence.Weekly;
        }

        if (intervals.All(i => Math.Abs(i - BiweeklyNominalDays) <= BiweeklyToleranceDays)
            && SameWeekdayThroughout(cluster))
        {
            return IncomeCadence.Biweekly;
        }

        if (intervals.All(i => i >= SemimonthlyMinDays && i <= SemimonthlyMaxDays))
        {
            return IncomeCadence.Semimonthly;
        }

        if (intervals.All(i => i >= MonthlyMinDays && i <= MonthlyMaxDays))
        {
            return IncomeCadence.Monthly;
        }

        return null;
    }

    private static bool SameWeekdayThroughout(List<(DateTimeOffset Date, decimal Amount)> cluster)
    {
        var first = cluster[0].Date.DayOfWeek;
        return cluster.All(d => d.Date.DayOfWeek == first);
    }
}
