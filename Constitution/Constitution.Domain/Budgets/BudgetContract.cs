namespace Constitution.Domain.Budgets;

/// <summary>
/// The wire between Index and Constitution for the month's grocery budget (PBI C-11.1.1, TF 1.12). The receiver, action and
/// permission names are frozen in <see cref="ConstitutionIdentity"/>; this carries the extras' names. Index writes the same
/// words in its sender, and <c>Contracts/BudgetContractTests</c> reads that file by content to hold the two equal.
/// </summary>
public static class BudgetContract
{
    /// <summary>The month the figures are for, <c>yyyy-MM</c> in the invariant culture.</summary>
    public const string MonthExtra = "month";

    /// <summary>The month's grocery budget as exact invariant decimal text, never a double.</summary>
    public const string BudgetExtra = "budget";

    /// <summary>What has been spent so far this month, in the same exact text.</summary>
    public const string SpentExtra = "spent";

    /// <summary>The extras, in the order the wire lists them.</summary>
    public static IReadOnlyList<string> Extras { get; } = [MonthExtra, BudgetExtra, SpentExtra];
}
