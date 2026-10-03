namespace Index.Domain.Worksheet;

/// <summary>
/// D100/D153, Guard G4: "income minus every category (including the debt extra-payment and savings
/// contributions) should balance to zero, or the worksheet flags that it doesn't." Every figure on the
/// page is already whole dollars (D154), so the check is exact <c>long</c> equality — never a tolerance
/// band, which is exactly the bug this guard exists to catch (a fuzzy "close enough" would silently
/// call a genuinely unbalanced page balanced).
/// </summary>
public static class ZeroBalanceCheck
{
    /// <summary>Each written figure is floored individually before summing (the same rule
    /// <see cref="WholeDollars.SumOfFlooredParts"/> and Guard G19 already establish) — never the raw
    /// decimal total floored once at the end, which can disagree with the sum of the already-printed,
    /// already-floored parts.</summary>
    public static long WrittenTotal(IEnumerable<decimal> writtenLimits) => WholeDollars.SumOfFlooredParts(writtenLimits);

    /// <summary>Positive: over by this many dollars. Negative: under (unassigned) by this many dollars.
    /// Zero: balanced.</summary>
    public static long Difference(IEnumerable<decimal> writtenLimits, long targetWholeDollars) =>
        WrittenTotal(writtenLimits) - targetWholeDollars;

    /// <summary>Exactly zero, in whole-dollar <c>long</c> arithmetic — no tolerance band. Watched to
    /// fail (Guard G4) against exactly the naive substitution: comparing two <c>decimal</c>/<c>double</c>
    /// totals within some small epsilon instead of requiring them to match to the cent.</summary>
    public static bool IsBalanced(IEnumerable<decimal> writtenLimits, long targetWholeDollars) =>
        Difference(writtenLimits, targetWholeDollars) == 0;
}
