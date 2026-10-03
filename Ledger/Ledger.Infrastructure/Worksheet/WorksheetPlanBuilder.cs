using Index.Domain.Model;
using Index.Domain.Time;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// Gathers everything <see cref="WorksheetPlan"/> needs for one target month (D151/D154/D155): the
/// active categories in their printed order, last month's actuals beside them, the detected income
/// projection (or "not confident"), and the Snowball's weekly payment total &#215; the Fridays that
/// actually fall in the target month (Guard G20). Talks to <see cref="BudgetService"/>,
/// <see cref="IIncomeDetectionService"/> and <see cref="SnowballService"/> only through their own public
/// methods — never a second, competing path into the same data.
/// </summary>
public sealed class WorksheetPlanBuilder
{
    private readonly BudgetService budget;
    private readonly IIncomeDetectionService income;
    private readonly SnowballService snowball;
    private readonly SavingsService savings;
    private readonly IClock clock;

    public WorksheetPlanBuilder(
        BudgetService budget, IIncomeDetectionService income, SnowballService snowball, SavingsService savings, IClock clock)
    {
        this.budget = budget;
        this.income = income;
        this.snowball = snowball;
        this.savings = savings;
        this.clock = clock;
    }

    public async Task<WorksheetPlan> BuildAsync(string targetYearMonth, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);

        var categories = await budget.GetActiveCategoriesAsync(cancellationToken);

        // "Last month" relative to the TARGET month, matching mockup 12's own "September" column on
        // the worksheet that opens October.
        var previousYearMonth = BudgetPeriod.PreviousYearMonth(targetYearMonth);
        var previousMonth = await budget.FindMonthAsync(previousYearMonth, cancellationToken);
        var previousAmounts = previousMonth is null
            ? []
            : await budget.GetCategoryAmountsAsync(previousMonth.Id, cancellationToken);
        var previousByCategory = previousAmounts.ToDictionary(a => a.CategoryId, a => a.ActualAmount);

        var lines = categories
            .Select(c => new WorksheetCategoryLine(c.Id, c.Name, previousByCategory.GetValueOrDefault(c.Id, 0m)))
            .ToList();
        var lastMonthSpending = lines.Sum(l => l.LastMonthActual);

        var estimate = await income.DetectAsync(cancellationToken);

        // Appendix A's own D13 total: the sum of every Snowball loan's own weekly payment — and nothing
        // else. The plan's overall extra payment is deliberately NOT in it: the extra is a box on this
        // same page (Set aside), one of the lines the target is spent on, so deducting it here as well
        // would count it twice — once out of the target, once in the boxes the target is checked
        // against. Mockup 13 is built that way: $9,350 less 5 x $1,250 = $3,100, with the $350 extra
        // payment (and the $490 written in (c)) sitting inside that $3,100.
        var loansInFocusOrder = await snowball.GetLoansInFocusOrderAsync(cancellationToken);
        var weeklyPaymentTotal = loansInFocusOrder.Sum(x => x.Account.NextPaymentAmount ?? 0m);

        var fridayCount = LeftToAssignCalculator.FridaysInMonth(targetYearMonth);
        var paymentsWhole = WholeDollars.Floor(weeklyPaymentTotal * fridayCount);
        var lastMonthWhole = WholeDollars.Floor(lastMonthSpending);

        long incomeWhole;
        long? leftToAssign;
        if (estimate is { IsConfident: true, ProjectedMonthlyIncome: { } projected })
        {
            var calc = LeftToAssignCalculator.Calculate(projected, weeklyPaymentTotal, fridayCount, lastMonthSpending);
            incomeWhole = calc.IncomeWholeDollars;
            leftToAssign = calc.LeftToAssignWholeDollars;
        }
        else
        {
            // D103: "not sure" — nothing is guessed onto the page, so there is no income figure to
            // subtract from, and "left to assign" is omitted rather than printed against a number Index
            // does not actually have (this task's own judgement call — the mockup does not draw this
            // state, since its own income is always confident).
            incomeWhole = 0;
            leftToAssign = null;
        }

        var setAsideLines = await BuildSetAsideLinesAsync(loansInFocusOrder, cancellationToken);

        return new WorksheetPlan(
            targetYearMonth, today, incomeWhole, estimate.IsConfident,
            fridayCount, paymentsWhole, lastMonthWhole, leftToAssign, lines, setAsideLines);
    }

    /// <summary>
    /// D98/D99, Release H: the "Set aside" section's own lines — the current focus loan's extra
    /// payment (reusing the SAME <paramref name="loansInFocusOrder"/> and <c>AccountStatus.IsPaidOff</c>
    /// this method's caller already fetched/relies on, never a second, competing focus-order
    /// computation), and one line per savings goal the owner has set.
    ///
    /// <para><b>"Last month's figure" for each line is a judgement call</b> (this task's own brief invites
    /// one where the mockup doesn't pin it down): the extra payment has no per-month history of its own
    /// (<see cref="SnowballService"/> tracks only the CURRENT configured amount, not a month-by-month
    /// record), so its "last month" figure is that same current amount — consistent with the mockup's
    /// own worksheet always printing the currently-configured figure next to the box for the month
    /// ahead. A savings goal's "last month" figure uses its recent monthly PACE
    /// (<see cref="SavingsAccountView.MonthlyPace"/>, Release G's own D83 projection) rather than a raw
    /// month-over-month balance delta, since Savings has no notion of "this month's planned contribution"
    /// distinct from balance growth, and pace is the closest already-computed figure to it.</para>
    /// </summary>
    private async Task<IReadOnlyList<WorksheetSetAsideLine>> BuildSetAsideLinesAsync(
        IReadOnlyList<(Account Account, SnowballPlan Plan)> loansInFocusOrder, CancellationToken cancellationToken)
    {
        var lines = new List<WorksheetSetAsideLine>();

        // D98: the extra debt payment, named for the Snowball's CURRENT focus loan — reusing the same
        // focus-order list and paid-off predicate SnowballService's own summary logic uses, never a
        // second implementation of "which loan is focused".
        var focusLoan = loansInFocusOrder.FirstOrDefault(x => !AccountStatus.IsPaidOff(x.Account));
        if (focusLoan.Account is not null)
        {
            var extra = await snowball.GetExtraPaymentAmountAsync(cancellationToken);
            lines.Add(new WorksheetSetAsideLine(
                SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment,
                "Extra debt payment", $"to {focusLoan.Account.Name}", extra));
        }

        // D99: one line per savings goal the owner has set.
        var savingsAccounts = await savings.GetAccountsAsync(cancellationToken);
        foreach (var goalAccount in savingsAccounts.Accounts.Where(a => a.GoalTargetAmount is not null))
        {
            lines.Add(new WorksheetSetAsideLine(
                SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(goalAccount.AccountId),
                "Savings", goalAccount.AccountName, goalAccount.MonthlyPace));
        }

        return lines;
    }
}
