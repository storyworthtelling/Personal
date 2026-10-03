using Index.Domain.Model;

namespace Index.Domain.Household;

/// <summary>
/// Her Budget month (mockup 11 — "everything not listed looks and behaves as in mockups 2-10"), worked
/// out from the household file by the SAME arithmetic the owner's page uses
/// (<see cref="BudgetMonthFigures.Compute"/>), so the two phones cannot disagree. The snapshot's categories
/// are given throwaway ids here only so they can go through that one function; nothing outside this view
/// ever sees them (the file carries no ids on purpose — they would address rows on the other phone's
/// database).
///
/// <para><see cref="DaysLeft"/> is measured against the SNAPSHOT's own month, never the month today
/// happens to be in: a file for September read on October 2nd says the month is over (0), not that 29
/// days of October remain under September's name.</para>
/// </summary>
public sealed record HouseholdBudgetView(BudgetMonthFigures Figures, int DaysLeft)
{
    public static HouseholdBudgetView From(HouseholdBudgetSummary summary, DateOnly today)
    {
        var categories = summary.Categories
            .Select((c, index) => new BudgetCategory { Id = Guid.NewGuid(), Name = c.Name, SortOrder = index })
            .ToList();

        var amounts = new List<BudgetCategoryAmount>(summary.Categories.Count);
        var lastMonth = new Dictionary<Guid, decimal>();
        for (var i = 0; i < summary.Categories.Count; i++)
        {
            var source = summary.Categories[i];
            amounts.Add(new BudgetCategoryAmount
            {
                CategoryId = categories[i].Id,
                LimitAmount = source.LimitAmount,
                ActualAmount = source.ActualAmount,
                TrackingOnly = source.TrackingOnly,
                LimitUnreadable = source.LimitUnreadable,
            });
            if (source.LastMonthAmount is { } previous)
            {
                lastMonth[categories[i].Id] = previous;
            }
        }

        var figures = BudgetMonthFigures.Compute(categories, amounts, summary.UncategorizedTotal, summary.UncategorizedCount, lastMonth);
        return new HouseholdBudgetView(figures, BudgetPeriod.DaysLeft(summary.YearMonth, today));
    }
}
