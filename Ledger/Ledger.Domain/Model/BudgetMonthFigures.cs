namespace Index.Domain.Model;

/// <summary>
/// The month line and the cards under it, worked out once so they cannot disagree (R-G17). The page used
/// to sum every <see cref="BudgetCategoryAmount"/> row of the month while drawing cards only for ACTIVE
/// categories, so a category the owner had removed (D55's soft delete keeps its amounts) still counted in
/// the month's "spent" and "of" figures with no card to explain them — and an unreadable-limit lookup over
/// the same unfiltered rows threw for it. Everything here is restricted to the categories passed in, which
/// are the ones the page draws.
///
/// <para><see cref="LastMonthByCategory"/> is last month's recorded spend per visible category (D142's
/// tracking-only comparison row), from the same rows the month-by-month history reads (D66's kept
/// totals). A category with no recorded last month has NO entry — absence is "never measured", which is
/// not the same as a measured zero.</para>
/// </summary>
public sealed record BudgetMonthFigures(
    IReadOnlyList<BudgetCategory> Categories,
    IReadOnlyList<BudgetCategoryAmount> Amounts,
    decimal TotalAgreed,
    decimal TotalSpent,
    decimal UncategorizedTotal,
    int UncategorizedCount,
    IReadOnlyDictionary<Guid, decimal>? LastMonthByCategory = null)
{
    /// <summary>The visible categories whose limit the worksheet reader could not make out (D49).</summary>
    public IReadOnlyList<BudgetCategory> UnreadableLimits =>
        Categories.Where(c => Amounts.Any(a => a.CategoryId == c.Id && a.LimitUnreadable)).ToList();

    public static BudgetMonthFigures Compute(
        IReadOnlyList<BudgetCategory> activeCategories,
        IReadOnlyList<BudgetCategoryAmount> monthAmounts,
        decimal uncategorizedTotal,
        int uncategorizedCount,
        IReadOnlyDictionary<Guid, decimal>? lastMonthByCategory = null)
    {
        var visible = activeCategories.Select(c => c.Id).ToHashSet();
        var amounts = monthAmounts.Where(a => visible.Contains(a.CategoryId)).ToList();
        var totalAgreed = amounts.Where(a => !a.TrackingOnly).Sum(a => a.LimitAmount);
        var totalSpent = amounts.Sum(a => a.ActualAmount) + uncategorizedTotal;
        var lastMonth = (lastMonthByCategory ?? new Dictionary<Guid, decimal>())
            .Where(entry => visible.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        return new BudgetMonthFigures(activeCategories, amounts, totalAgreed, totalSpent, uncategorizedTotal, uncategorizedCount, lastMonth);
    }
}
