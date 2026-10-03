namespace Index.Domain.Model;

/// <summary>
/// D82/D83, mockup 16: whether a savings goal is ever reached at the account's own recent pace, and
/// when. <see cref="Date"/> is the worked-out month; <see cref="IsNotAtThisPace"/> is the honest phrase
/// the post-it and the card print instead of a date decades out ("Reaches it: not at this pace") when
/// the pace is flat or falling.
/// </summary>
public readonly record struct SavingsReachesIt(DateOnly? Date, bool IsNotAtThisPace)
{
    public static SavingsReachesIt At(DateOnly date) => new(date, IsNotAtThisPace: false);

    public static SavingsReachesIt NotAtThisPace() => new(Date: null, IsNotAtThisPace: true);
}

/// <summary>
/// D83: the ONLY projection Index ships this phase — the account's own recent growth (deposits and
/// interest together), never the interest-rate path D82 also describes. D82's path needs Plaid to
/// report a deposit account's interest rate, which mockup 9's own open question says Plaid probably
/// does not (its Liabilities data covers cards/loans/mortgages, not deposit accounts) — unverified,
/// so that path is not attempted here; see INDEX-P11-PLAN.md Release G's own remark.
///
/// <para>Pure and DB-free (<see cref="SavingsSnapshot"/> rows in, a pace and a date out), so Guard G23
/// (<c>SavingsPaceTests</c>) exercises the one place a flat/falling pace could otherwise divide by a
/// non-positive number.</para>
/// </summary>
public static class SavingsPace
{
    /// <summary>D81/mockup 16: the pace window is at most the last twelve RECORDED months —
    /// "the last twelve months' growth" when there are that many, "the last 4 months'" (mockup 16's own
    /// example) when there are fewer.</summary>
    public const int MaxWindowMonths = 12;

    /// <summary>
    /// The monthly pace and how many months of history it is drawn from, from up to the last
    /// <see cref="MaxWindowMonths"/> of an account's monthly <see cref="SavingsSnapshot"/> rows.
    /// Fewer than two distinct months is nothing to measure a rate from, so it reports a flat (zero)
    /// pace over zero months — not a special case callers must check for first, since a zero pace
    /// already reads correctly everywhere a pace is used (<see cref="ReachesIt"/> below, a chart with
    /// nothing to project).
    /// </summary>
    public static (decimal MonthlyPace, int MonthsOfHistory) ComputePace(IReadOnlyList<SavingsSnapshot> history)
    {
        var ordered = history.OrderBy(snapshot => snapshot.MonthOf).ToList();
        var windowed = ordered.Count > MaxWindowMonths + 1
            ? ordered.Skip(ordered.Count - (MaxWindowMonths + 1)).ToList()
            : ordered;

        if (windowed.Count < 2)
        {
            return (0m, 0);
        }

        var first = windowed[0];
        var last = windowed[^1];
        var months = MonthsBetween(first.MonthOf, last.MonthOf);
        if (months <= 0)
        {
            return (0m, 0);
        }

        return ((last.Balance - first.Balance) / months, months);
    }

    private static int MonthsBetween(DateOnly earlier, DateOnly later) =>
        ((later.Year - earlier.Year) * 12) + (later.Month - earlier.Month);

    /// <summary>
    /// Guard G23: given the account's current balance, its goal, and the pace <see cref="ComputePace"/>
    /// found, when is the goal reached? Already at or past the goal reports THIS month rather than
    /// inventing a special "reached" state mockup 16 never draws. Short of the goal with a flat or
    /// falling pace (&lt;= 0) is <see cref="SavingsReachesIt.NotAtThisPace"/> — never a division by a
    /// non-positive number and never a date computed from one.
    /// </summary>
    public static SavingsReachesIt ReachesIt(
        decimal currentBalance, decimal targetAmount, decimal monthlyPace, DateOnly asOf)
    {
        var remaining = targetAmount - currentBalance;

        // NAIVE (Guard G23 watched red against exactly this — see SavingsPaceTests): a flat or
        // falling pace was never checked, so a zero pace threw DivideByZeroException (decimal
        // division by zero throws, unlike double, which would silently produce Infinity) and a
        // negative pace silently computed a negative month count that landed on a PAST date instead
        // of failing loudly — the naive line was simply:
        //   var monthsNeeded = (int)Math.Ceiling(remaining / monthlyPace);
        //   return SavingsReachesIt.At(asOf.AddMonths(monthsNeeded));

        if (remaining <= 0m)
        {
            return SavingsReachesIt.At(asOf);
        }

        if (monthlyPace <= 0m)
        {
            return SavingsReachesIt.NotAtThisPace();
        }

        var monthsNeeded = (int)Math.Ceiling(remaining / monthlyPace);
        return SavingsReachesIt.At(asOf.AddMonths(monthsNeeded));
    }
}
