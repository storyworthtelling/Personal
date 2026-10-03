using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Services;

namespace Index.Infrastructure.Household;

/// <summary>
/// The two household-file series (version 2) and the owner-side shapes they stand for, converted both
/// ways in one place (remediation 3.6, owner decision R7). The owner's install turns
/// <see cref="SnowballHistory"/> and <see cref="SavingsAccountGrowth"/> into the file's compact form when
/// it publishes; hers turns the compact form back into the SAME two types and hands them to the same
/// chart views — which is why her charts are the owner's and not a second drawing that could drift.
///
/// <para>Pure and database-free, so the conversions are tested without a phone. The file stores aligned
/// arrays (weeks once, a balance per week per loan) to stay small; what is NOT stored is whatever the
/// reading side can recompute exactly — a loan's peak (the maximum of its balances) and a savings goal's
/// projected month (<see cref="SavingsPace.ReachesIt"/> from the pace the Accounts summary already
/// carries) — because a stored copy of a derived figure is one more thing to drift from its source.</para>
/// </summary>
public static class HouseholdChartSeries
{
    // ---- owner -> file (the publisher) ----

    /// <summary>Compacts the owner's History into the file's form, keeping the most recent
    /// <see cref="HouseholdSnowballHistory.MaxWeeks"/> weeks if there are more (there are 70 today).</summary>
    public static HouseholdSnowballHistory FromSnowballHistory(SnowballHistory history)
    {
        var skip = Math.Max(0, history.Weeks.Count - HouseholdSnowballHistory.MaxWeeks);
        return new HouseholdSnowballHistory(
            history.Weeks.Skip(skip).ToList(),
            history.LoansInFocusOrder.Select(loan => new HouseholdSnowballLoanHistory(
                loan.Name, loan.IsMortgage, loan.IsPaidOff,
                loan.Points.Skip(skip).Select(point => point.Balance).ToList())).ToList());
    }

    /// <summary>Compacts each savings account's Growth chart into the file's form, keeping the most recent
    /// <see cref="HouseholdSavingsGrowth.MaxMonths"/> months.</summary>
    public static IReadOnlyList<HouseholdSavingsGrowth> FromSavingsGrowth(IReadOnlyList<SavingsAccountGrowth> growth) =>
        growth.Select(account =>
        {
            var points = account.Points.TakeLast(HouseholdSavingsGrowth.MaxMonths).ToList();
            return new HouseholdSavingsGrowth(
                account.InstitutionName, account.AccountName, account.GoalTargetAmount,
                points.Select(point => point.MonthOf).ToList(),
                points.Select(point => point.Balance).ToList());
        }).ToList();

    // ---- file -> hers (the pages) ----

    /// <summary>The file's History back into the type the owner's charts consume. The account id is
    /// empty on purpose: the file carries none (it would address a row on the other phone's database), and
    /// no chart reads it.</summary>
    public static SnowballHistory ToSnowballHistory(HouseholdSnowballHistory history) =>
        new(history.Weeks,
            history.LoansInFocusOrder.Select(loan =>
            {
                var points = history.Weeks.Select((week, i) => new SnowballHistoryPoint(week, loan.Balances[i])).ToList();
                var peak = points.Count > 0 ? points.Max(point => point.Balance) : 0m;
                return new SnowballLoanHistory(Guid.Empty, loan.Name, points, peak, loan.IsPaidOff, loan.IsMortgage);
            }).ToList());

    /// <summary>The file's Growth series back into the owner's type, or null when the file has none
    /// (version 1). The dotted projection is worked out here, as of <paramref name="asOf"/>, from the
    /// balance and pace the Accounts summary carries for the same account — the figures her card already
    /// prints — falling back to the series itself for an account the summary does not list.</summary>
    public static IReadOnlyList<SavingsAccountGrowth>? ToSavingsGrowth(HouseholdSnapshot snapshot, DateOnly asOf)
    {
        if (snapshot.SavingsGrowth is null)
        {
            return null;
        }

        return snapshot.SavingsGrowth.Select(account =>
        {
            var points = account.Months
                .Select((month, i) => new SavingsHistoryPoint(month, account.Balances[i]))
                .ToList();

            SavingsReachesIt? reachesIt = null;
            if (account.GoalTargetAmount is { } goal)
            {
                var card = snapshot.Savings.Accounts.FirstOrDefault(a =>
                    string.Equals(a.InstitutionName, account.InstitutionName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.AccountName, account.AccountName, StringComparison.OrdinalIgnoreCase));

                var (balance, pace) = card is not null
                    ? (card.Balance, card.MonthlyPace)
                    : (points.Count > 0 ? points[^1].Balance : 0m, PaceOf(points));
                reachesIt = SavingsPace.ReachesIt(balance, goal, pace, asOf);
            }

            return new SavingsAccountGrowth(
                Guid.Empty, account.InstitutionName, account.AccountName, points, account.GoalTargetAmount, reachesIt);
        }).ToList();
    }

    private static decimal PaceOf(IReadOnlyList<SavingsHistoryPoint> points) =>
        SavingsPace.ComputePace(
            points.Select(point => new SavingsSnapshot { MonthOf = point.MonthOf, Balance = point.Balance }).ToList()).MonthlyPace;
}
