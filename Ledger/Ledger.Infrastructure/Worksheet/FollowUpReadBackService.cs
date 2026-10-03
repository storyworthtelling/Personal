using System.Globalization;
using Microsoft.Extensions.Logging;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;

namespace Index.Infrastructure.Worksheet;

public sealed record FollowUpReadBackOutcome(bool Attempted, bool DocumentFound, int LinesApplied);

/// <summary>
/// D153/D156: reads back whatever is hand-written on the currently-delivered follow-up page and applies
/// it. The one rule that makes this a DISTINCT service from <see cref="WorksheetReadBackService"/> rather
/// than a shared code path: <b>an empty (or illegible) box takes the printed suggestion</b>, never "leave
/// it alone" — the ordinary worksheet's own rule for a blank box (D151: "a box left empty keeps last
/// month's figure") is the opposite of D156's for this page ("an empty box takes the suggestion"). The
/// two must never be merged into one "blank means X" branch.
/// </summary>
public sealed class FollowUpReadBackService
{
    private const double ConfidenceThreshold = 0.75;

    private readonly BudgetService budget;
    private readonly SnowballService snowball;
    private readonly IRemarkableClient remarkable;
    private readonly Func<string, IHandwritingExtractor> extractorFactory;
    private readonly ILogger<FollowUpReadBackService>? logger;

    public FollowUpReadBackService(
        BudgetService budget, SnowballService snowball, IRemarkableClient remarkable,
        Func<string, IHandwritingExtractor> extractorFactory, ILogger<FollowUpReadBackService>? logger = null)
    {
        this.budget = budget;
        this.snowball = snowball;
        this.remarkable = remarkable;
        this.extractorFactory = extractorFactory;
        this.logger = logger;
    }

    public async Task<FollowUpReadBackOutcome> ReadBackAsync(FollowUpPlan plan, Guid budgetMonthId, CancellationToken cancellationToken)
    {
        RemarkableDoc? doc;
        try
        {
            doc = await remarkable.DownloadByTitle(WorksheetTitles.FollowUpCurrent);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Follow-up read-back could not reach the reMarkable for {YearMonth}", plan.TargetYearMonth);
            return new FollowUpReadBackOutcome(true, false, 0);
        }

        if (doc is null)
        {
            // Nothing out there yet — the ordinary case the FIRST time a follow-up cycle runs for a
            // month, before any follow-up page has ever been delivered.
            return new FollowUpReadBackOutcome(true, false, 0);
        }

        // Named as the page prints them, so two savings lines are "Savings (Emergency fund)" and
        // "Savings (Summer trip)" to the reader rather than "Savings" twice.
        var actionableLines = plan.ActionableLines.Select(l => (l.Key, Name: l.PrintedName)).ToList();
        var unreadableKeys = plan.ResendLines.Select(l => l.Key).ToHashSet();
        var instruction = FollowUpReadBackInstruction.Build(actionableLines, plan.ReferenceLines.Count, unreadableKeys, plan.TargetYearMonth);
        var handwriting = extractorFactory(instruction);

        IReadOnlyList<ParsedNote> notes;
        try
        {
            notes = await handwriting.Extract(doc.PdfBytes);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Handwriting extraction failed for the {YearMonth} follow-up", plan.TargetYearMonth);
            return new FollowUpReadBackOutcome(true, true, 0);
        }

        var byColumn = notes.Where(n => !string.IsNullOrEmpty(n.Column)).ToDictionary(n => n.Column!, n => n);
        var applied = 0;

        foreach (var line in plan.ActionableLines)
        {
            var finalKey = FollowUpRegions.FinalBox(line.Key);

            if (line.WasUnreadable)
            {
                // D49, R-G25: a line sent back because its limit could not be read has no suggestion for a
                // blank box to take, and the stored figure is exactly what nobody could read — so neither
                // may stand in for the owner. A box that reads is the limit (and clears the flag); a box
                // with ink that STILL cannot be read stays flagged, to be asked again; a blank box is
                // simply not written yet and stays flagged too.
                if (!byColumn.TryGetValue(finalKey, out var again))
                {
                    continue;
                }
                if (again.Confidence >= ConfidenceThreshold && TryParseAmount(again.Text, out var rewritten))
                {
                    await ApplyAsync(line.Key, rewritten, budgetMonthId, cancellationToken);
                    applied++;
                }
                else
                {
                    await MarkUnreadableAsync(line.Key, budgetMonthId, cancellationToken);
                }
                continue;
            }

            decimal value;
            if (byColumn.TryGetValue(finalKey, out var note)
                && note.Confidence >= ConfidenceThreshold
                && TryParseAmount(note.Text, out var written))
            {
                value = written;
            }
            else
            {
                // D156's own wording: "an empty box takes the suggestion" — the box was left blank (or
                // couldn't be read), so the printed suggestion is what applies, NOT the figure already
                // on file (which is what a blank means on the ordinary worksheet).
                value = line.SuggestedWholeDollars ?? line.WrittenWholeDollars;
            }

            await ApplyAsync(line.Key, value, budgetMonthId, cancellationToken);
            applied++;
        }

        // "Change another line": a name matched against any printed line (actionable or reference) —
        // categories and set-aside lines alike, read the same way as the worksheet's own write-in
        // category matching.
        var everyLine = plan.ActionableLines.Concat(plan.ReferenceLines).ToList();
        for (var i = 0; i < FollowUpRegions.ChangeAnotherLines; i++)
        {
            if (!byColumn.TryGetValue(FollowUpRegions.ChangeAnotherName(i), out var nameNote)
                || nameNote.Confidence < ConfidenceThreshold)
            {
                continue;
            }
            if (!byColumn.TryGetValue(FollowUpRegions.ChangeAnotherAmount(i), out var amountNote)
                || amountNote.Confidence < ConfidenceThreshold
                || !TryParseAmount(amountNote.Text, out var amount))
            {
                continue;
            }

            var name = nameNote.Text.Trim();
            var matched = everyLine.FirstOrDefault(l =>
                string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)
                || (l.Detail is not null && string.Equals(l.Detail, name, StringComparison.OrdinalIgnoreCase)));
            if (matched is null)
            {
                continue;
            }

            await ApplyAsync(matched.Key, amount, budgetMonthId, cancellationToken);
            applied++;
        }

        return new FollowUpReadBackOutcome(true, true, applied);
    }

    private async Task MarkUnreadableAsync(string key, Guid budgetMonthId, CancellationToken cancellationToken)
    {
        if (key.StartsWith("set-aside:savings:", StringComparison.Ordinal)
            && Guid.TryParse(key["set-aside:savings:".Length..], out var savingsAccountId))
        {
            await budget.MarkSavingsSetAsideLimitUnreadableAsync(budgetMonthId, savingsAccountId, cancellationToken);
        }
        else if (key.StartsWith("category:", StringComparison.Ordinal)
            && Guid.TryParse(key["category:".Length..], out var categoryId))
        {
            await budget.MarkLimitUnreadableAsync(budgetMonthId, categoryId, cancellationToken);
        }
    }

    private async Task ApplyAsync(string key, decimal value, Guid budgetMonthId, CancellationToken cancellationToken)
    {
        if (key == WorksheetRegions.SetAsideExtraPayment)
        {
            await snowball.SetExtraPaymentAmountAsync(value, cancellationToken);
        }
        else if (key.StartsWith("set-aside:savings:", StringComparison.Ordinal)
            && Guid.TryParse(key["set-aside:savings:".Length..], out var savingsAccountId))
        {
            await budget.SetSavingsSetAsideLimitAsync(budgetMonthId, savingsAccountId, value, cancellationToken);
        }
        else if (key.StartsWith("category:", StringComparison.Ordinal)
            && Guid.TryParse(key["category:".Length..], out var categoryId))
        {
            await budget.SetCategoryLimitAsync(budgetMonthId, categoryId, value, cancellationToken);
        }
    }

    private static bool TryParseAmount(string text, out decimal amount)
    {
        var cleaned = text.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
    }
}
