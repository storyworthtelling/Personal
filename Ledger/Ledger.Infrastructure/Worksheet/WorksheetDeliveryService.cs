using Microsoft.Extensions.Logging;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Storyworthtelling.Remarkable;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// One monthly worksheet cycle (D52/D60/D89/D155), run by <c>WorksheetDeliveryWorker</c> on the same
/// daily wakeup <c>RefreshWorker</c> uses. Every wake: the follow-up cycle, then the first read-back of
/// a freshly delivered worksheet (the morning after it was printed, D153). On the day
/// <see cref="WorksheetDeliverySchedule"/> says is due:
/// <list type="number">
/// <item><description>(the current month's worksheet has by now been read by the step above, applying
/// limits/income-corrections/new categories through <see cref="WorksheetReadBackService"/>;)</description></item>
/// <item><description>composes NEXT month's worksheet from a fresh <see cref="WorksheetPlan"/>;</description></item>
/// <item><description>delivers it to the reMarkable, replacing whatever was there (D60);</description></item>
/// <item><description>records the printed category order on the new month, so a future read-back
/// can reconstruct the same boxes (Guard G18).</description></item>
/// </list>
/// </summary>
public sealed class WorksheetDeliveryService
{
    private readonly BudgetService budget;
    private readonly WorksheetPlanBuilder planBuilder;
    private readonly WorksheetReadBackService readBack;
    private readonly SnowballService snowball;
    private readonly FollowUpPlanBuilder followUpPlanBuilder;
    private readonly FollowUpReadBackService followUpReadBack;
    private readonly IRemarkableClient remarkable;
    private readonly IClock clock;
    private readonly ILogger<WorksheetDeliveryService>? logger;

    public WorksheetDeliveryService(
        BudgetService budget,
        WorksheetPlanBuilder planBuilder,
        WorksheetReadBackService readBack,
        SnowballService snowball,
        FollowUpPlanBuilder followUpPlanBuilder,
        FollowUpReadBackService followUpReadBack,
        IRemarkableClient remarkable,
        IClock clock,
        ILogger<WorksheetDeliveryService>? logger = null)
    {
        this.budget = budget;
        this.planBuilder = planBuilder;
        this.readBack = readBack;
        this.snowball = snowball;
        this.followUpPlanBuilder = followUpPlanBuilder;
        this.followUpReadBack = followUpReadBack;
        this.remarkable = remarkable;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Three steps on the same daily wakeup (this task's own brief), in this order: the follow-up cycle
    /// (D153) runs every day regardless of the calendar; the first read-back of a freshly delivered
    /// worksheet runs every day too (<see cref="RunFirstReadBackAsync"/>); and the monthly worksheet cycle
    /// runs only on the day <see cref="WorksheetDeliverySchedule"/> says is due. None gates another.
    ///
    /// <para>The follow-up goes BEFORE the first read-back on purpose: the read on Oct 1 is what starts a
    /// month's follow-up cycle, and mockup 13 draws that page as "printed Oct 2" — the next morning, not
    /// the morning of the read.</para>
    /// </summary>
    public async Task<WorksheetDeliveryResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        await RunFollowUpCycleAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);
        var firstRead = await RunFirstReadBackAsync(today, cancellationToken);

        if (!WorksheetDeliverySchedule.IsDueToday(today))
        {
            return WorksheetDeliveryResult.NotDue;
        }

        // The current month's worksheet — the one delivered a cycle ago, targeting today's own calendar
        // month — was read above if it was still unread. When that read was this very wake, its outcome
        // is what this cycle reports; otherwise (already read on an earlier morning) there is nothing new
        // to report, and the sheet is NOT read a second time: its ink would be put back over any
        // correction a follow-up has made since.
        var currentYearMonth = BudgetPeriod.YearMonthOf(today);
        var readBackOutcome = firstRead is { } read && read.YearMonth == currentYearMonth
            ? read.Outcome
            : new WorksheetReadBackOutcome(false, false, 0, 0, 0, 0);

        var targetYearMonth = WorksheetDeliverySchedule.TargetYearMonth(today);
        var plan = await planBuilder.BuildAsync(targetYearMonth, cancellationToken);
        var pdfBytes = WorksheetPainter.Paint(plan);

        var delivered = await TryDeliverAsync(pdfBytes, cancellationToken);
        if (delivered)
        {
            var month = await budget.GetOrCreateMonthAsync(targetYearMonth, cancellationToken);
            var extraPaymentLine = plan.SetAsideLinesOrEmpty.FirstOrDefault(l => l.Kind == Domain.Worksheet.SetAsideLineKind.ExtraDebtPayment);
            await budget.RecordWorksheetDeliveryAsync(
                month.Id, plan.CategoryIdsInPrintedOrder, cancellationToken,
                plan.SetAsideKeysInPrintedOrder, plan.PrintedTargetWholeDollars,
                extraPaymentLine is null ? null : Domain.Worksheet.WholeDollars.Floor(extraPaymentLine.LastMonthActual));

            // Release H: a savings set-aside limit needs a persisted row to apply a read-back limit to,
            // the same reason EnsureCategoryAmountsAsync exists for categories.
            // Each row starts at the figure the page printed beside it (R-G14), so a box the owner leaves
            // blank — "an empty box keeps last month's figure" — is read back as that figure.
            var savingsLines = plan.SetAsideLinesOrEmpty
                .Where(l => l.Kind == Domain.Worksheet.SetAsideLineKind.Savings)
                .Select(l => (
                    SavingsAccountId: Guid.Parse(l.Key[WorksheetRegions.SetAsideSavingsPrefix.Length..]),
                    PrintedFigure: (decimal)Domain.Worksheet.WholeDollars.Floor(l.LastMonthActual)))
                .ToList();
            await budget.EnsureSavingsSetAsideAmountsAsync(month.Id, savingsLines, cancellationToken);
        }

        return new WorksheetDeliveryResult(
            true, targetYearMonth, delivered,
            readBackOutcome.DocumentFound ? readBackOutcome.LimitsApplied : null,
            readBackOutcome.DocumentFound ? readBackOutcome.LimitsMarkedUnreadable : null,
            readBackOutcome.DocumentFound ? readBackOutcome.CategoriesCreated : null);
    }

    /// <summary>
    /// D153, Guard R-G13: the first read-back of a freshly delivered worksheet. Every wake, find the
    /// worksheet on the tablet that nobody has read (<see cref="BudgetService.FindWorksheetAwaitingFirstReadBackAsync"/>)
    /// and, from the morning after it was printed, read it: its limits go live at once and the month
    /// moves to AwaitingConfirmation (or Approved if it already balances), which is what hands it to the
    /// follow-up cycle. Silent, like the rest of the round-trip (D89).
    ///
    /// <para>Idempotent by the month's own status: a read that finds no document, or whose extraction
    /// fails, leaves the month where it was, so the next wake tries again; a read that completes moves
    /// the month on, so the sheet is never read twice. Returns null when nothing is due.</para>
    /// </summary>
    private async Task<(string YearMonth, WorksheetReadBackOutcome Outcome)?> RunFirstReadBackAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        BudgetMonth? month;
        try
        {
            month = await budget.FindWorksheetAwaitingFirstReadBackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not check whether a delivered worksheet is waiting to be read");
            return null;
        }

        if (month is null || !WorksheetDeliverySchedule.IsFirstReadBackDue(today, month.YearMonth, month.Status))
        {
            return null;
        }

        try
        {
            var outcome = await readBack.ReadBackAsync(month.YearMonth, cancellationToken);
            logger?.LogInformation(
                "First read-back of the {YearMonth} worksheet: document found {Found}, {Applied} limit(s) applied",
                month.YearMonth, outcome.DocumentFound, outcome.LimitsApplied);
            return (month.YearMonth, outcome);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Worksheet read-back failed for {YearMonth}", month.YearMonth);
            return (month.YearMonth, new WorksheetReadBackOutcome(true, false, 0, 0, 0, 0));
        }
    }

    // D60: this month's page replaces last month's — the SAME fixed title, so finding the previous
    // one and trashing it before uploading the new one under that title is a literal replace, not a
    // title-pattern search across accumulated documents (Foreword's dated-document idiom does not
    // apply here — Index's worksheet is one document, always).
    private async Task<bool> TryDeliverAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        try
        {
            var folderId = await remarkable.EnsureFolder(WorksheetTitles.Folder);

            var previous = await remarkable.DownloadByTitle(WorksheetTitles.Current);
            if (previous is not null)
            {
                await remarkable.MoveToTrash(previous.Id);
            }

            await remarkable.Upload(WorksheetTitles.Current, pdfBytes, folderId);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Worksheet delivery failed");
            return false;
        }
    }

    /// <summary>
    /// D153: the second, independent trigger. Every day, whether or not the monthly cycle also runs:
    /// find the one month (there is only ever one) whose worksheet went through a real balance check and
    /// hasn't balanced yet; read back whatever is CURRENTLY on the reMarkable under the follow-up's own
    /// title (a harmless no-op the very first time, before any follow-up has ever been delivered — D153's
    /// own "arrives the next day" is exactly this first delivery); re-run the balance check against
    /// whatever is now persisted; and if it still doesn't balance, deliver a FRESH follow-up — built from
    /// today's own figures, since a previous follow-up's corrections may have just changed them — for
    /// tomorrow. This is what makes the follow-up "repeat each morning until the figures balance" (D153):
    /// each day this finds the same month again, unless yesterday's read-back finally balanced it.
    /// </summary>
    public async Task<FollowUpCycleResult> RunFollowUpCycleAsync(CancellationToken cancellationToken)
    {
        Index.Domain.Model.BudgetMonth? month;
        try
        {
            // Two reasons a month can still be owed a page, checked in this order: it has not balanced
            // yet (D153), or it has been read and a limit in it could not be made out (D49, R-G25) —
            // which is true of a month that DID balance too, so the second question cannot be asked of
            // the first one's answer.
            month = await budget.FindMonthNeedingFollowUpAsync(cancellationToken)
                ?? await budget.FindMonthWithUnreadableLimitsAsync(
                    BudgetPeriod.YearMonthOf(DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date)), cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not check whether a follow-up is due");
            return FollowUpCycleResult.NotNeeded;
        }

        if (month is null)
        {
            return FollowUpCycleResult.NotNeeded;
        }

        var planBeforeReadBack = await followUpPlanBuilder.BuildAsync(month, cancellationToken);
        if (planBeforeReadBack is null)
        {
            // Nothing was printed to balance against and nothing is waiting to be read again: no
            // page can have been sent for this month, so there is nothing to read back or deliver.
            return FollowUpCycleResult.NotNeeded;
        }

        try
        {
            await followUpReadBack.ReadBackAsync(planBeforeReadBack, month.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Follow-up read-back failed for {YearMonth}", month.YearMonth);
        }

        // The zero check, where there is a target to check against. A month that had balanced (Approved)
        // and then took a newly-written limit that unbalances it goes back to the follow-up cycle, which
        // is what makes a re-read box that changes the total ask the owner about the total.
        bool? balanced = null;
        if (month.PrintedZeroBasedTargetWholeDollars is { } target)
        {
            var writtenLimits = (await budget.GetWrittenLimitsForBalanceCheckAsync(month.Id, cancellationToken)).ToList();
            writtenLimits.Add(await snowball.GetExtraPaymentAmountAsync(cancellationToken));

            if (ZeroBalanceCheck.IsBalanced(writtenLimits, target))
            {
                await budget.MarkBalancedAsync(month.Id, cancellationToken);
                balanced = true;
            }
            else
            {
                if (month.Status == BudgetMonthStatus.Approved)
                {
                    await budget.MarkAwaitingConfirmationAsync(month.Id, cancellationToken);
                }
                balanced = false;
            }
        }

        var freshPlan = await followUpPlanBuilder.BuildAsync(month, cancellationToken);
        if (freshPlan is null)
        {
            // Balanced (or nothing to balance against) with no unread limit left: the cycle is finished.
            return new FollowUpCycleResult(true, month.YearMonth, Balanced: balanced, Delivered: false);
        }

        if (balanced != false && !freshPlan.HasResend)
        {
            // "A page that balances gets no reply" (mockup 13) — and no limit is waiting to be asked again.
            return new FollowUpCycleResult(true, month.YearMonth, Balanced: balanced, Delivered: false);
        }

        var pdfBytes = FollowUpPainter.Paint(freshPlan);
        var delivered = await TryDeliverFollowUpAsync(pdfBytes, cancellationToken);
        return new FollowUpCycleResult(true, month.YearMonth, Balanced: balanced, Delivered: delivered);
    }

    // D153: "each new page replaces the last on the reMarkable" — the follow-up's own fixed title, the
    // exact same replace-by-title mechanics as the worksheet's own TryDeliverAsync above.
    private async Task<bool> TryDeliverFollowUpAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        try
        {
            var folderId = await remarkable.EnsureFolder(WorksheetTitles.Folder);

            var previous = await remarkable.DownloadByTitle(WorksheetTitles.FollowUpCurrent);
            if (previous is not null)
            {
                await remarkable.MoveToTrash(previous.Id);
            }

            await remarkable.Upload(WorksheetTitles.FollowUpCurrent, pdfBytes, folderId);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Follow-up delivery failed");
            return false;
        }
    }
}

/// <summary>What one day's follow-up check did — <see cref="Ran"/> is false when no month currently
/// needs one (the ordinary case: no zero-based cycle is active, or it already balanced).</summary>
public sealed record FollowUpCycleResult(bool Ran, string? YearMonth, bool? Balanced, bool? Delivered)
{
    public static readonly FollowUpCycleResult NotNeeded = new(false, null, null, null);
}
