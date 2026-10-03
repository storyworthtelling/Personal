using SkiaSharp;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Storyworthtelling.Paper;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// Draws the follow-up page (mockup 13(b)/(c), Release H) through <see cref="VectorPdfSink"/>, the same
/// Document colour profile and <see cref="PaperTypeScale"/> the worksheet itself uses — a sibling
/// composer, not a rewrite of <see cref="WorksheetPainter"/> (this task's own brief).
/// </summary>
public static class FollowUpPainter
{
    public static IReadOnlyList<WorksheetBox> BoxesFor(FollowUpPlan plan) =>
        FollowUpRegions.AllBoxes(plan.ActionableKeysInPrintedOrder, plan.ReferenceLines.Count);

    public static byte[] Paint(FollowUpPlan plan)
    {
        using var sink = new VectorPdfSink();
        var canvas = sink.BeginPage(FollowUpRegions.PageWidth, FollowUpRegions.PageHeight);
        Draw(canvas, plan);
        sink.EndPage();
        return sink.Close();
    }

    private static void Draw(SKCanvas canvas, FollowUpPlan plan)
    {
        using var ink = new PageInk();
        var (labelFont, titleFont, bodyFont, secondaryFont, monoFont) = (ink.Label, ink.Title, ink.Body, ink.Secondary, ink.Mono);
        var (inkPaint, softPaint, mutedPaint, boxPaint) = (ink.Ink, ink.Soft, ink.Muted, ink.Box);

        var left = FollowUpRegions.MarginLeft;
        var right = left + FollowUpRegions.BodyWidth;
        var monthYear = WorksheetDates.MonthYear(plan.TargetYearMonth);
        var monthWord = WorksheetDates.MonthWord(plan.TargetYearMonth);
        var previousMonth = WorksheetDates.PreviousMonthWord(plan.TargetYearMonth);
        var directionWord = plan.IsOver ? "over" : "to place";

        // ---- header ----
        canvas.DrawText(Heading(plan.TargetYearMonth), left, FollowUpRegions.MarginTop + PaperTypeScale.Label, labelFont, mutedPaint);
        var headline = plan.IsResendOnly
            ? $"{monthWord}, {ResendCountPhrase(plan.ResendLines.Count)} to write again"
            : $"{monthWord}, {PlanMoney.Whole(plan.DifferenceWholeDollars)} {directionWord}";
        canvas.DrawText(
            headline,
            left, FollowUpRegions.MarginTop + PaperTypeScale.Label + PaperTypeScale.Display + TitleDrop, titleFont, inkPaint);
        var ruleY = FollowUpRegions.MarginTop + FollowUpRegions.HeaderHeight - 8f;
        PageInk.Rule(canvas, ink.IdentityRule, left, right, ruleY);

        // ---- intro (mockup 13(b)/(c)'s own worked sentences) ----
        var introY = ruleY + 16f;
        if (plan.IsResendOnly)
        {
            // D49: the page exists only to ask again. Nothing about over or under — the month either
            // balances or has no target — and nothing is suggested for a figure nobody has read.
            var count = plan.ResendLines.Count;
            canvas.DrawText(
                count == 1
                    ? "A limit on the worksheet couldn't be read, so it's asked again here."
                    : $"{count} limits on the worksheet couldn't be read, so they're asked again here.",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                count == 1 ? "Write it again in its box below." : "Write each one again in its box below.",
                left, introY + 15f, bodyFont, softPaint);
            canvas.DrawText("Everything else stands as written.", left, introY + 30f, bodyFont, softPaint);
        }
        else if (plan.IsOver)
        {
            canvas.DrawText(
                $"What we wrote comes to {PlanMoney.Whole(plan.WrittenTotalWholeDollars)}, and there's {PlanMoney.Whole(plan.TargetWholeDollars)} to give a job.",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"These are the lines furthest above {previousMonth}'s spending, brought back until it balances.",
                left, introY + 15f, bodyFont, softPaint);
            canvas.DrawText(
                plan.HasResend
                    ? "An empty box takes the suggestion; a line marked not read needs your figure again."
                    : "Leave a box empty to take the suggestion, or write your own figure.",
                left, introY + 30f, bodyFont, softPaint);
        }
        else if (plan.RemainderGoesToExtraPayment)
        {
            canvas.DrawText(
                $"What we wrote comes to {PlanMoney.Whole(plan.WrittenTotalWholeDollars)}, and there's {PlanMoney.Whole(plan.TargetWholeDollars)} to give a job,",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"so {PlanMoney.Whole(plan.DifferenceWholeDollars)} has none yet. It's suggested for the Snowball's focus.",
                left, introY + 15f, bodyFont, softPaint);
            canvas.DrawText(
                plan.HasResend
                    ? "Empty boxes take that; a line marked not read needs your figure again."
                    : "Leave the boxes empty to take that, or split it however you like.",
                left, introY + 30f, bodyFont, softPaint);
        }
        else
        {
            // No Snowball loan, so no extra payment line to suggest the remainder for (D156 names no
            // other home for it): say so plainly rather than promising a suggestion the page does not make.
            canvas.DrawText(
                $"What we wrote comes to {PlanMoney.Whole(plan.WrittenTotalWholeDollars)}, and there's {PlanMoney.Whole(plan.TargetWholeDollars)} to give a job,",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"so {PlanMoney.Whole(plan.DifferenceWholeDollars)} has none yet. There's no suggestion for it:",
                left, introY + 15f, bodyFont, softPaint);
            canvas.DrawText(
                "put it on a line below, or on one of the rows above.",
                left, introY + 30f, bodyFont, softPaint);
        }

        // ---- the actionable table (mockup's LINE / SEPT / WROTE / SUGGEST / FINAL): the number columns
        // are right-aligned against their cells, FINAL sits over its box ----
        var tableHeaderBaseline = FollowUpRegions.TableTop - FollowUpRegions.TableHeaderHeight + PaperTypeScale.Label;
        canvas.DrawText("LINE", left, tableHeaderBaseline, labelFont, mutedPaint);
        canvas.DrawText(WorksheetDates.PreviousMonthShort(plan.TargetYearMonth).ToUpperInvariant(), FollowUpRegions.PreviousRight, tableHeaderBaseline, SKTextAlign.Right, labelFont, mutedPaint);
        canvas.DrawText("WROTE", FollowUpRegions.WroteRight, tableHeaderBaseline, SKTextAlign.Right, labelFont, mutedPaint);
        canvas.DrawText("SUGGEST", FollowUpRegions.SuggestRight, tableHeaderBaseline, SKTextAlign.Right, labelFont, mutedPaint);
        canvas.DrawText("FINAL", FollowUpRegions.FinalBoxLeft, tableHeaderBaseline, labelFont, mutedPaint);
        PageInk.Rule(canvas, ink.InkRule, left, right, FollowUpRegions.TableTop - PageInk.HeavyRule / 2);

        var boxes = FollowUpRegions.ActionableBoxes(plan.ActionableKeysInPrintedOrder);
        for (var i = 0; i < plan.ActionableLines.Count; i++)
        {
            var line = plan.ActionableLines[i];
            var box = boxes[i];
            var centre = box.Top + box.Height / 2;
            var numberBaseline = centre + 4f;
            var hasDetail = !string.IsNullOrEmpty(line.Detail);
            var nameBaseline = hasDetail ? centre - 2f : numberBaseline;

            canvas.DrawText(line.TableTitle, left, nameBaseline, bodyFont, inkPaint);
            if (hasDetail)
            {
                canvas.DrawText(line.Detail!, left, nameBaseline + 12f, secondaryFont, mutedPaint);
            }
            canvas.DrawText(PlanMoney.Whole(line.LastMonthWholeDollars), FollowUpRegions.PreviousRight, numberBaseline, SKTextAlign.Right, monoFont, softPaint);
            // A figure the suggestion changes reads struck through on the page, not erased, so what was
            // written can still be seen (D156); a dash means the line stands as written.
            //
            // A line whose limit could not be read has no written figure to show: saying "$0" would be
            // claiming a reading that was never made, so the column says what happened instead.
            if (line.WasUnreadable)
            {
                canvas.DrawText(UnreadMark, FollowUpRegions.WroteRight, numberBaseline, SKTextAlign.Right, secondaryFont, mutedPaint);
            }
            else
            {
                canvas.DrawText(PlanMoney.Whole(line.WrittenWholeDollars), FollowUpRegions.WroteRight, numberBaseline, SKTextAlign.Right, monoFont, line.IsSuggested ? mutedPaint : softPaint);
            }
            if (line.IsSuggested)
            {
                var writtenWidth = monoFont.MeasureText(PlanMoney.Whole(line.WrittenWholeDollars));
                canvas.DrawLine(FollowUpRegions.WroteRight - writtenWidth, numberBaseline - 4f, FollowUpRegions.WroteRight, numberBaseline - 4f, mutedPaint);
            }
            canvas.DrawText(line.IsSuggested ? PlanMoney.Whole(line.SuggestedWholeDollars!.Value) : "—", FollowUpRegions.SuggestRight, numberBaseline, SKTextAlign.Right, ink.MonoBold, inkPaint);

            DrawBox(canvas, boxPaint, box);
            canvas.DrawText("$", box.Left + 6f, centre + 4f, monoFont, mutedPaint);
            PageInk.Rule(canvas, ink.RowRule, left, right, box.Top - (FollowUpRegions.RowHeight - box.Height) / 2 + FollowUpRegions.RowHeight + PageInk.LightRule / 2);
        }

        // ---- everything below the table is placed by FollowUpRegions.Layout, the one sum that also
        // positions the "change another line" boxes (Guard R-G12) ----
        var layout = FollowUpRegions.Layout(plan.ActionableLines.Count, plan.ReferenceLines.Count);

        // "With the suggestion(s)" — a page that only asks for unread limits suggests nothing, so it has
        // no such line (the space it would take stays, so the layout is the one FollowUpRegions decides).
        if (!plan.IsResendOnly)
        {
            var suggestionsWord = plan.ActionableLines.Count(l => !l.WasUnreadable) == 1 ? "suggestion" : "suggestions";
            canvas.DrawText($"With the {suggestionsWord}", left, layout.WithSuggestionsBaseline, ink.SecondaryBold, inkPaint);
            var balancedText = plan.WithSuggestionsBalances ? $"{PlanMoney.Whole(plan.TargetWholeDollars)} · balanced" : PlanMoney.Whole(plan.TargetWholeDollars);
            canvas.DrawText(balancedText, right, layout.WithSuggestionsBaseline, SKTextAlign.Right, monoFont, inkPaint);
        }

        // "Everything else, as written" — two columns, each line labelled for what it is (a savings line
        // by its account or goal, mockup 13(b)'s "Emergency fund" / "Summer trip", not "Savings" twice)
        if (layout.ReferenceHeaderBaseline is { } referenceHeader)
        {
            DrawSectionLabel(canvas, ink, "EVERYTHING ELSE, AS WRITTEN", referenceHeader);
            for (var i = 0; i < plan.ReferenceLines.Count; i++)
            {
                var line = plan.ReferenceLines[i];
                var column = i % FollowUpRegions.ReferenceColumns;
                var row = i / FollowUpRegions.ReferenceColumns;
                var y = layout.ReferenceFirstRowBaseline + row * FollowUpRegions.ReferenceRowPitch;
                canvas.DrawText(line.ReferenceLabel, FollowUpRegions.ReferenceColumnLeft(column), y, secondaryFont, inkPaint);
                canvas.DrawText(PlanMoney.Whole(line.WrittenWholeDollars), FollowUpRegions.ReferenceColumnRight(column), y, SKTextAlign.Right, monoFont, softPaint);
                PageInk.Rule(
                    canvas, ink.RowRule, FollowUpRegions.ReferenceColumnLeft(column), FollowUpRegions.ReferenceColumnRight(column),
                    y + ReferenceRuleDrop);
            }
        }

        // "Change another line" — label, then the boxes it labels, then the sentence under them
        DrawSectionLabel(canvas, ink, "CHANGE ANOTHER LINE", layout.ChangeHeaderBaseline);

        var changeBoxes = FollowUpRegions.ChangeAnotherLineBoxes(plan.ActionableLines.Count, plan.ReferenceLines.Count);
        foreach (var (nameBox, amountBox) in changeBoxes)
        {
            DrawBox(canvas, boxPaint, nameBox);
            DrawBox(canvas, boxPaint, amountBox);
            canvas.DrawText("$", amountBox.Left + 6f, amountBox.Top + amountBox.Height / 2 + 4f, monoFont, mutedPaint);
            PageInk.Rule(canvas, ink.RowRule, left, right, amountBox.Top + amountBox.Height + (FollowUpRegions.RowHeight - amountBox.Height) / 2 + PageInk.LightRule / 2);
        }

        var footerSentence = plan.IsResendOnly
            ? "If a limit still can't be read, another page like this comes tomorrow."
            : plan.HasResend
                ? $"If the final figures still don't come to {PlanMoney.Whole(plan.TargetWholeDollars)}, or a limit can't be read, another page like this comes tomorrow."
                : $"If the final figures still don't come to {PlanMoney.Whole(plan.TargetWholeDollars)}, another page like this comes tomorrow.";
        canvas.DrawText(footerSentence, left, layout.FooterSentenceBaseline, secondaryFont, softPaint);

        // ---- footer ----
        var footerY = FollowUpRegions.PageHeight - FollowUpRegions.MarginBottom + 18f;
        canvas.DrawText($"Index · {monthYear} follow-up", left, footerY, secondaryFont, mutedPaint);
        // .folio .r — the date is mono, against the right margin, and always English.
        canvas.DrawText($"printed {WorksheetDates.PrintedOn(plan.PrintedOn)}", right, footerY, SKTextAlign.Right, monoFont, mutedPaint);
    }

    /// <summary>Gap from the title line's size to its baseline, as on the worksheet.</summary>
    private const float TitleDrop = 5f;

    /// <summary>From a reference line's baseline to its hairline (<c>.fu-rest div</c>'s 19px line).</summary>
    private const float ReferenceRuleDrop = 5.5f;

    /// <summary>A section label (<c>.ws-sub</c>) with its 1.5pt ink rule just under it.</summary>
    private static void DrawSectionLabel(SKCanvas canvas, PageInk ink, string label, float baseline)
    {
        canvas.DrawText(label, FollowUpRegions.MarginLeft, baseline, ink.Label, ink.Muted);
        PageInk.Rule(canvas, ink.InkRule, FollowUpRegions.MarginLeft, FollowUpRegions.MarginLeft + FollowUpRegions.BodyWidth, baseline + 5f);
    }

    private static void DrawBox(SKCanvas canvas, SKPaint paint, WorksheetBox box) =>
        canvas.DrawRoundRect(new SKRect(box.Left, box.Top, box.Left + box.Width, box.Top + box.Height), 3f, 3f, paint);

    /// <summary>The small heading at the top of the page. One source for the painter and for the read-back
    /// instruction, which tells the reader to refuse a page whose heading names another month.</summary>
    public static string Heading(string yearMonth) => $"INDEX · {WorksheetDates.MonthYear(yearMonth).ToUpperInvariant()} · FOLLOW-UP";

    /// <summary>What the "wrote" column says for a limit that was never read (see the row drawing).</summary>
    internal const string UnreadMark = "not read";

    private static string ResendCountPhrase(int count) => count == 1 ? "1 limit" : $"{count} limits";

}
