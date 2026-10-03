using System.Globalization;
using Microsoft.Extensions.Logging;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;

namespace Index.Infrastructure.Worksheet;

/// <summary>What one read-back pass did, for the delivery worker's own log line — never surfaced to
/// the owner (D89: the whole round-trip is silent).</summary>
public sealed record WorksheetReadBackOutcome(
    bool Attempted, bool DocumentFound, int BoxesRead, int LimitsApplied, int LimitsMarkedUnreadable, int CategoriesCreated);

/// <summary>
/// D49/D54: reads back whatever is hand-written on the currently-delivered worksheet and applies it —
/// existing categories' limits, the income correction, and D151's write-in categories — through
/// <see cref="BudgetService"/>'s own public methods only (never a second path into
/// <see cref="Index.Infrastructure.Persistence.LedgerDbContext"/>, per the repo's architecture rules).
/// A box the reader can't make out is never guessed: <see cref="BudgetService.MarkLimitUnreadableAsync"/>
/// flags it, which Release E's existing dog-ear wiring (<c>BudgetPage.RefreshNotices</c>) already turns
/// into a Waiting-page notice with no further wiring needed here.
///
/// <para><b>Why an extractor FACTORY, not one injected <see cref="IHandwritingExtractor"/>:</b> the
/// shared <c>ClaudeVisionHandwritingExtractor</c> fixes its <c>instruction</c> string once, at
/// construction — right for Foreword's stable planner-page prompt, wrong for Index's, since
/// <see cref="WorksheetReadBackInstruction.Build"/> depends on the RECORDED category order, which
/// varies month to month as categories are added, renamed or removed. So a fresh extractor is built
/// for each read-back call, with that call's own instruction, rather than one long-lived instance
/// whose instruction would silently go stale the first time the category list changed. Read-back runs
/// once a month, not once a page, so this costs a prompt-cache miss rather than the a per-turn one —
/// nowhere near the cost class Folio's own caching finding was written against.</para>
/// </summary>
public sealed class WorksheetReadBackService
{
    // D49's "never guessed": a transcription below this confidence is treated exactly like a blank or
    // illegible box, never applied as if it were a confident answer.
    private const double ConfidenceThreshold = 0.75;

    private readonly BudgetService budget;
    private readonly IRemarkableClient remarkable;
    private readonly Func<string, IHandwritingExtractor> extractorFactory;
    private readonly SnowballService? snowball;
    private readonly ILogger<WorksheetReadBackService>? logger;

    public WorksheetReadBackService(
        BudgetService budget, IRemarkableClient remarkable, Func<string, IHandwritingExtractor> extractorFactory,
        SnowballService? snowball = null, ILogger<WorksheetReadBackService>? logger = null)
    {
        this.budget = budget;
        this.remarkable = remarkable;
        this.extractorFactory = extractorFactory;
        this.snowball = snowball;
        this.logger = logger;
    }

    public async Task<WorksheetReadBackOutcome> ReadBackAsync(string yearMonth, CancellationToken cancellationToken)
    {
        var month = await budget.FindMonthAsync(yearMonth, cancellationToken);
        if (month is null)
        {
            return new WorksheetReadBackOutcome(false, false, 0, 0, 0, 0);
        }

        var order = await budget.GetWorksheetCategoryOrderAsync(month.Id, cancellationToken);
        if (order is null)
        {
            // Never delivered as a worksheet — nothing was printed for this month, so there is
            // nothing on the reMarkable to read back.
            return new WorksheetReadBackOutcome(false, false, 0, 0, 0, 0);
        }

        // Release H: the "Set aside" section's own box keys, in the order they were printed — null for
        // a worksheet delivered before Release H, or one with no confident income to check against.
        var setAsideOrder = await budget.GetWorksheetSetAsideOrderAsync(month.Id, cancellationToken) ?? [];

        RemarkableDoc? doc;
        try
        {
            doc = await remarkable.DownloadByTitle(WorksheetTitles.Current);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Worksheet read-back could not reach the reMarkable for {YearMonth}", yearMonth);
            return new WorksheetReadBackOutcome(true, false, 0, 0, 0, 0);
        }

        if (doc is null)
        {
            return new WorksheetReadBackOutcome(true, false, 0, 0, 0, 0);
        }

        // The month's category rows are what a read limit is written INTO (SetCategoryLimitAsync does
        // nothing for a category with no row), and until now the first thing to create them was the Budget
        // tab being opened in that month. The first read-back now happens the morning after the sheet is
        // printed — before anyone has opened the month — so the read makes sure they exist, exactly as it
        // already does for a write-in category below.
        await budget.EnsureCategoryAmountsAsync(month.Id, trackingOnly: false, cancellationToken);

        var instruction = WorksheetReadBackInstruction.Build(order, setAsideOrder);
        var handwriting = extractorFactory(instruction);

        IReadOnlyList<ParsedNote> notes;
        try
        {
            notes = await handwriting.Extract(doc.PdfBytes);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Handwriting extraction failed for the {YearMonth} worksheet", yearMonth);
            return new WorksheetReadBackOutcome(true, true, 0, 0, 0, 0);
        }

        var limitsApplied = 0;
        var limitsUnreadable = 0;
        var categoriesCreated = 0;

        var newNames = new Dictionary<int, (string Text, double Confidence)>();
        var newLimits = new Dictionary<int, (string Text, double Confidence)>();

        foreach (var note in notes)
        {
            var column = note.Column;
            if (string.IsNullOrEmpty(column))
            {
                continue; // a note with no box identity can't be placed anywhere safely
            }

            if (column == WorksheetRegions.IncomeCorrection)
            {
                if (note.Confidence >= ConfidenceThreshold && TryParseAmount(note.Text, out var income))
                {
                    await budget.SetIncomeOverrideAsync(month.Id, income, cancellationToken);
                }
                // Below threshold or unparseable: left alone. D103's projected figure stands for the
                // month — there is no "income unreadable" flag to raise (unlike a category's limit),
                // since the printed income was Index's own projection to begin with, not a blank the
                // owner was asked to fill.
                continue;
            }

            if (column.StartsWith("category:", StringComparison.Ordinal))
            {
                if (!Guid.TryParse(column["category:".Length..], out var categoryId))
                {
                    continue;
                }
                if (note.Confidence >= ConfidenceThreshold && TryParseAmount(note.Text, out var limit))
                {
                    await budget.SetCategoryLimitAsync(month.Id, categoryId, limit, cancellationToken);
                    limitsApplied++;
                }
                else
                {
                    await budget.MarkLimitUnreadableAsync(month.Id, categoryId, cancellationToken);
                    limitsUnreadable++;
                }
                continue;
            }

            if (column == WorksheetRegions.SetAsideExtraPayment)
            {
                // D98: the extra debt payment box writes straight through to the Snowball's own current
                // figure (SnowballPlan.ExtraPaymentAmount) — ONE reserved number, not two uncross-checked
                // ones. An illegible/unparseable box is simply left alone, the same as the income
                // correction box above: there is no dedicated "unreadable" flag on a Snowball figure to
                // raise (unlike a category's own limit).
                if (snowball is not null && note.Confidence >= ConfidenceThreshold && TryParseAmount(note.Text, out var extra))
                {
                    await snowball.SetExtraPaymentAmountAsync(extra, cancellationToken);
                    limitsApplied++;
                }
                continue;
            }

            if (column.StartsWith("set-aside:savings:", StringComparison.Ordinal)
                && Guid.TryParse(column["set-aside:savings:".Length..], out var savingsAccountId))
            {
                if (note.Confidence >= ConfidenceThreshold && TryParseAmount(note.Text, out var savingsLimit))
                {
                    await budget.SetSavingsSetAsideLimitAsync(month.Id, savingsAccountId, savingsLimit, cancellationToken);
                    limitsApplied++;
                }
                else
                {
                    await budget.MarkSavingsSetAsideLimitUnreadableAsync(month.Id, savingsAccountId, cancellationToken);
                    limitsUnreadable++;
                }
                continue;
            }

            if (column.StartsWith("new-name:", StringComparison.Ordinal) && int.TryParse(column["new-name:".Length..], out var nameIndex))
            {
                newNames[nameIndex] = (note.Text, note.Confidence);
                continue;
            }

            if (column.StartsWith("new-limit:", StringComparison.Ordinal) && int.TryParse(column["new-limit:".Length..], out var limitIndex))
            {
                newLimits[limitIndex] = (note.Text, note.Confidence);
            }
        }

        // D151: a legible new name becomes a category — matched against an existing one by name first,
        // so re-writing a category's own name in a blank line updates that line rather than duplicating
        // it. Its limit is applied if legible; if the limit is missing or couldn't be read, the category
        // still gets created and its limit is simply marked unreadable, so it is asked again next month
        // exactly the way an existing category's illegible limit already is (D49).
        foreach (var (index, (rawName, nameConfidence)) in newNames)
        {
            var name = rawName.Trim();
            if (name.Length == 0 || nameConfidence < ConfidenceThreshold)
            {
                continue; // an illegible name has nothing safe to create — never guessed
            }

            var existing = await budget.GetActiveCategoriesAsync(cancellationToken);
            var matched = existing.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            Guid categoryId;
            if (matched is null)
            {
                var created = await budget.AddCategoryAsync(name, cancellationToken);
                categoryId = created.Id;
                categoriesCreated++;
            }
            else
            {
                categoryId = matched.Id;
            }

            await budget.EnsureCategoryAmountsAsync(month.Id, trackingOnly: false, cancellationToken);

            if (newLimits.TryGetValue(index, out var limitNote)
                && limitNote.Confidence >= ConfidenceThreshold
                && TryParseAmount(limitNote.Text, out var newLimit))
            {
                await budget.SetCategoryLimitAsync(month.Id, categoryId, newLimit, cancellationToken);
                limitsApplied++;
            }
            else
            {
                await budget.MarkLimitUnreadableAsync(month.Id, categoryId, cancellationToken);
                limitsUnreadable++;
            }
        }

        await ApplyBalanceCheckAsync(month, cancellationToken);

        return new WorksheetReadBackOutcome(true, true, notes.Count, limitsApplied, limitsUnreadable, categoriesCreated);
    }

    /// <summary>
    /// D100/D153, Guard G4: once every box is read back and applied, sum every written limit —
    /// categories, the extra debt payment (read fresh off <see cref="SnowballService"/>, since that is
    /// where this read-back just wrote it) and every savings set-aside limit — and compare to the
    /// target this month's worksheet was actually printed against
    /// (<c>BudgetMonth.PrintedZeroBasedTargetWholeDollars</c>, never a freshly re-detected income figure,
    /// which could have drifted from what's on the physical sheet). Balanced moves the month to
    /// <see cref="BudgetMonthStatus.Approved"/>; anything else stays
    /// <see cref="BudgetMonthStatus.AwaitingConfirmation"/> — "the limits are live from the first read,
    /// and only the zero check is pending" (D156). A month with no printed target (a plan built before
    /// Release H, or income that wasn't confident) skips the check entirely and keeps Release F's own
    /// unconditional behaviour.
    /// </summary>
    private async Task ApplyBalanceCheckAsync(BudgetMonth month, CancellationToken cancellationToken)
    {
        if (month.PrintedZeroBasedTargetWholeDollars is not { } target)
        {
            await budget.MarkAwaitingConfirmationAsync(month.Id, cancellationToken);
            return;
        }

        var writtenLimits = (await budget.GetWrittenLimitsForBalanceCheckAsync(month.Id, cancellationToken)).ToList();
        if (snowball is not null)
        {
            writtenLimits.Add(await snowball.GetExtraPaymentAmountAsync(cancellationToken));
        }

        if (ZeroBalanceCheck.IsBalanced(writtenLimits, target))
        {
            await budget.MarkBalancedAsync(month.Id, cancellationToken);
        }
        else
        {
            await budget.MarkAwaitingConfirmationAsync(month.Id, cancellationToken);
        }
    }

    // Tolerates "$450", "450.00", "450", whitespace and thousands separators — whatever the model
    // hands back for a figure it read, never the literal dollar sign the box itself already printed.
    private static bool TryParseAmount(string text, out decimal amount)
    {
        var cleaned = text.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
    }
}
