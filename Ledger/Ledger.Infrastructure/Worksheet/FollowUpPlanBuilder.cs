using Index.Domain.Model;
using Index.Domain.Time;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// D153/D156: gathers everything <see cref="FollowUpPlan"/> needs for one month whose worksheet did
/// not balance — the current, live "written" figures (whatever is persisted right now, which may
/// already include a previous follow-up's own corrections), each line's stable "last month" reference,
/// and D156's suggestions from <see cref="FollowUpSuggestionEngine"/>. Called only once a read-back has
/// proven the month unbalanced (<see cref="ZeroBalanceCheck.IsBalanced"/> false) — building a plan for a
/// balanced month is the caller's bug, not this type's to guard against, the same trust the rest of this
/// pipeline places in its own callers.
/// </summary>
public sealed class FollowUpPlanBuilder
{
    private readonly BudgetService budget;
    private readonly SnowballService snowball;
    private readonly SavingsService savings;
    private readonly IClock clock;

    public FollowUpPlanBuilder(BudgetService budget, SnowballService snowball, SavingsService savings, IClock clock)
    {
        this.budget = budget;
        this.snowball = snowball;
        this.savings = savings;
        this.clock = clock;
    }

    public async Task<FollowUpPlan?> BuildAsync(BudgetMonth month, CancellationToken cancellationToken)
    {
        var hasTarget = month.PrintedZeroBasedTargetWholeDollars is not null;
        var target = month.PrintedZeroBasedTargetWholeDollars ?? 0L;

        var today = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);

        // ---- categories: "written" is whatever is persisted right now; "last month" is the previous
        // calendar month's own actual spending — stable regardless of how many follow-up corrections
        // this month has already had (Guard G21's own reasoning). ----
        var previousYearMonth = BudgetPeriod.PreviousYearMonth(month.YearMonth);
        var previousMonth = await budget.FindMonthAsync(previousYearMonth, cancellationToken);
        var previousAmounts = previousMonth is null
            ? []
            : await budget.GetCategoryAmountsAsync(previousMonth.Id, cancellationToken);
        var previousByCategory = previousAmounts.ToDictionary(a => a.CategoryId, a => a.ActualAmount);

        var categories = await budget.GetActiveCategoriesAsync(cancellationToken);
        var currentAmounts = (await budget.GetCategoryAmountsAsync(month.Id, cancellationToken)).ToDictionary(a => a.CategoryId);

        var categoryCandidates = categories
            .Where(c => currentAmounts.ContainsKey(c.Id))
            .Select(c => new FollowUpCandidateLine(
                WorksheetRegions.CategoryLimit(c.Id), c.Name, null,
                WholeDollars.Floor(previousByCategory.GetValueOrDefault(c.Id, 0m)),
                WholeDollars.Floor(currentAmounts[c.Id].LimitAmount)))
            .ToList();

        // ---- set-aside: only the lines this month's worksheet actually printed (D156: "an empty box
        // takes the suggestion" only ever applies to what was on the page). ----
        var setAsideOrder = await budget.GetWorksheetSetAsideOrderAsync(month.Id, cancellationToken) ?? [];

        FollowUpCandidateLine? extraPayment = null;
        if (setAsideOrder.Contains(WorksheetRegions.SetAsideExtraPayment))
        {
            var written = await snowball.GetExtraPaymentAmountAsync(cancellationToken);
            var lastMonth = month.PrintedExtraPaymentLastMonthWholeDollars ?? WholeDollars.Floor(written);
            // Mockup 13(c): the table names the loan under the title. The same "which loan is focused" the
            // worksheet printed — the first loan in focus order that is not yet paid off.
            var loansInFocusOrder = await snowball.GetLoansInFocusOrderAsync(cancellationToken);
            var focusLoan = loansInFocusOrder.FirstOrDefault(x => !AccountStatus.IsPaidOff(x.Account));
            var loanName = focusLoan.Account?.Name;
            extraPayment = new FollowUpCandidateLine(
                WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", loanName, lastMonth, WholeDollars.Floor(written));
        }

        var savingsAmounts = await budget.GetSavingsSetAsideAmountsAsync(month.Id, cancellationToken);
        var savingsAccountsByKey = (await savings.GetAccountsAsync(cancellationToken)).Accounts.ToDictionary(a => a.AccountId);
        var savingsCandidates = savingsAmounts
            .Where(a => savingsAccountsByKey.ContainsKey(a.SavingsAccountId))
            // The rows come back in database order; the page lists them as the worksheet did (the
            // recorded printed order), so two savings lines never swap places from one morning to the next.
            .OrderBy(a => PrintedPosition(setAsideOrder, WorksheetRegions.SetAsideSavings(a.SavingsAccountId)))
            .Select(a =>
            {
                var account = savingsAccountsByKey[a.SavingsAccountId];
                // D83's own recent-pace projection stands in for "last month's figure" the same way
                // WorksheetPlanBuilder's own judgement call does for the original worksheet — see its
                // remarks for why (Savings has no notion of a monthly CONTRIBUTION distinct from
                // balance growth, and pace is the closest already-computed figure to it).
                return new FollowUpCandidateLine(
                    WorksheetRegions.SetAsideSavings(a.SavingsAccountId), "Savings", account.AccountName,
                    WholeDollars.Floor(account.MonthlyPace), WholeDollars.Floor(a.LimitAmount));
            })
            .ToList();

        static int PrintedPosition(IReadOnlyList<string> printedOrder, string key)
        {
            for (var i = 0; i < printedOrder.Count; i++)
            {
                if (printedOrder[i] == key)
                {
                    return i;
                }
            }
            return int.MaxValue;
        }

        // ---- D49, R-G25: the lines whose limit could not be read. They are NOT suggestion candidates —
        // a stored figure the reader never made out is not a figure to trim or to build a suggestion on —
        // so they leave the candidate lists and come back as boxed rows of their own, each with no
        // suggestion, asking for the figure again. Their stored limit still counts in the written total
        // (it is what the balance check has been summing). ----
        var unreadableKeys = currentAmounts.Values
            .Where(a => a.LimitUnreadable)
            .Select(a => WorksheetRegions.CategoryLimit(a.CategoryId))
            .Concat(savingsAmounts.Where(a => a.LimitUnreadable).Select(a => WorksheetRegions.SetAsideSavings(a.SavingsAccountId)))
            .ToHashSet();

        var resendLines = categoryCandidates.Concat(savingsCandidates)
            .Where(c => unreadableKeys.Contains(c.Key))
            .Select(c => new FollowUpSuggestion(
                c.Key, c.Name, c.Detail, c.LastMonthWholeDollars, c.WrittenWholeDollars, null, WasUnreadable: true))
            .ToList();

        if (!hasTarget && resendLines.Count == 0)
        {
            return null; // nothing was printed to balance against and nothing is waiting to be read again
        }

        var allWritten = categoryCandidates.Select(c => (decimal)c.WrittenWholeDollars)
            .Concat(extraPayment is null ? [] : [(decimal)extraPayment.WrittenWholeDollars])
            .Concat(savingsCandidates.Select(s => (decimal)s.WrittenWholeDollars));
        var writtenTotal = ZeroBalanceCheck.WrittenTotal(allWritten);
        var difference = hasTarget ? writtenTotal - target : 0L;

        // The candidates the suggestions are worked out over: everything except the unread lines.
        var suggestibleCategories = categoryCandidates.Where(c => !unreadableKeys.Contains(c.Key)).ToList();
        var suggestibleSavings = savingsCandidates.Where(c => !unreadableKeys.Contains(c.Key)).ToList();

        List<FollowUpSuggestion> actionable;
        List<FollowUpSuggestion> reference;

        if (difference > 0)
        {
            // Over: only the spending categories are ever trimmable — set-aside lines are NEVER handed
            // to SuggestOver at all (D156's own rule, structural per FollowUpSuggestionEngine's remarks).
            var suggestions = FollowUpSuggestionEngine.SuggestOver(suggestibleCategories, difference);
            // In trim order (largest rise first), the order the page lists them in — see InTrimOrder.
            actionable = FollowUpSuggestionEngine.InTrimOrder(suggestions.Where(s => s.IsSuggested)).ToList();
            reference = suggestions.Where(s => !s.IsSuggested).ToList();
            if (extraPayment is not null)
            {
                reference.Add(new FollowUpSuggestion(extraPayment.Key, extraPayment.Name, extraPayment.Detail, extraPayment.LastMonthWholeDollars, extraPayment.WrittenWholeDollars, null));
            }
            reference.AddRange(suggestibleSavings.Select(s => new FollowUpSuggestion(s.Key, s.Name, s.Detail, s.LastMonthWholeDollars, s.WrittenWholeDollars, null)));
        }
        else if (difference < 0)
        {
            // Under: the whole remainder is suggested onto the extra payment; savings lines are shown
            // boxed too (each with its own Final box) even though untouched, so the owner can split it
            // differently instead (D156).
            //
            // A sheet with no Snowball loan has no extra payment line, and D156 names no other home for
            // the remainder. The savings lines are then the actionable rows on their own — still boxed,
            // still untouched — so the table is never empty, and the page says (FollowUpPainter) that it
            // makes no suggestion. Delivering an empty table every morning would be a page with nothing to
            // act on.
            var savingsAsRows = suggestibleSavings.Select(s => new FollowUpSuggestion(
                s.Key, s.Name, s.Detail, s.LastMonthWholeDollars, s.WrittenWholeDollars, null)).ToList();
            if (extraPayment is not null)
            {
                var (extraSuggestion, savingsSuggestions) = FollowUpSuggestionEngine.SuggestUnder(extraPayment, suggestibleSavings, -difference);
                actionable = new List<FollowUpSuggestion> { extraSuggestion };
                actionable.AddRange(savingsSuggestions);
            }
            else
            {
                actionable = savingsAsRows;
            }
            reference = suggestibleCategories.Select(c => new FollowUpSuggestion(c.Key, c.Name, c.Detail, c.LastMonthWholeDollars, c.WrittenWholeDollars, null)).ToList();
        }
        else
        {
            // Balanced after all (the caller only builds a plan for an unbalanced month, but a read-back
            // can have just balanced it): nothing to suggest, everything is reference.
            actionable = [];
            reference = suggestibleCategories.Concat(suggestibleSavings)
                .Select(c => new FollowUpSuggestion(c.Key, c.Name, c.Detail, c.LastMonthWholeDollars, c.WrittenWholeDollars, null))
                .ToList();
            if (extraPayment is not null)
            {
                reference.Add(new FollowUpSuggestion(extraPayment.Key, extraPayment.Name, extraPayment.Detail, extraPayment.LastMonthWholeDollars, extraPayment.WrittenWholeDollars, null));
            }
        }

        // The unread lines lead the table: they are the one thing on the page that cannot be settled by
        // leaving a box empty.
        actionable.InsertRange(0, resendLines);

        return new FollowUpPlan(
            month.YearMonth, today, IsOver: difference > 0, Math.Abs(difference), target, writtenTotal, actionable, reference, hasTarget);
    }
}
