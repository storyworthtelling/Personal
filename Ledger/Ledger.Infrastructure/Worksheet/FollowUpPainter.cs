using System.Globalization;
using SkiaSharp;
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
        var ink = SKColor.Parse(PaperColorTokens.Ink);
        var inkSoft = SKColor.Parse(PaperColorTokens.InkSoft);
        var inkMuted = SKColor.Parse(PaperColorTokens.InkMuted);
        var identityRule = SKColor.Parse(PaperColorTokens.PositiveDoc);

        using var labelFont = new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label);
        using var titleFont = new SKFont(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Title);
        using var bodyFont = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        using var secondaryFont = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
        using var monoFont = new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Mono);

        using var inkPaint = new SKPaint { Color = ink, IsAntialias = true };
        using var softPaint = new SKPaint { Color = inkSoft, IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = inkMuted, IsAntialias = true };
        using var rulePaint = new SKPaint { Color = identityRule, IsAntialias = true };
        using var boxPaint = new SKPaint { Color = inkMuted, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

        var left = FollowUpRegions.MarginLeft;
        var right = left + FollowUpRegions.BodyWidth;
        var monthDisplayName = MonthDisplayName(plan.TargetYearMonth);
        var directionWord = plan.IsOver ? "over" : "to place";

        // ---- header ----
        canvas.DrawText(Heading(plan.TargetYearMonth), left, FollowUpRegions.MarginTop + PaperTypeScale.Label, labelFont, mutedPaint);
        var headline = plan.IsResendOnly
            ? $"{monthDisplayName.Split(' ')[0]}, {ResendCountPhrase(plan.ResendLines.Count)} to write again"
            : $"{monthDisplayName.Split(' ')[0]}, {Money(plan.DifferenceWholeDollars)} {directionWord}";
        canvas.DrawText(
            headline,
            left, FollowUpRegions.MarginTop + PaperTypeScale.Label + PaperTypeScale.Title + 2f, titleFont, inkPaint);
        var ruleY = FollowUpRegions.MarginTop + FollowUpRegions.HeaderHeight - 8f;
        canvas.DrawLine(left, ruleY, right, ruleY, rulePaint);

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
                $"What we wrote comes to {Money(plan.WrittenTotalWholeDollars)}, and there's {Money(plan.TargetWholeDollars)} to give a job.",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                "These are the lines furthest above last month's, brought back until it balances.",
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
                $"What we wrote comes to {Money(plan.WrittenTotalWholeDollars)}, and there's {Money(plan.TargetWholeDollars)} to give a job,",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"so {Money(plan.DifferenceWholeDollars)} has none yet. It's suggested for the Snowball's focus.",
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
                $"What we wrote comes to {Money(plan.WrittenTotalWholeDollars)}, and there's {Money(plan.TargetWholeDollars)} to give a job,",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"so {Money(plan.DifferenceWholeDollars)} has none yet. There's no suggestion for it:",
                left, introY + 15f, bodyFont, softPaint);
            canvas.DrawText(
                "put it on a line below, or on one of the rows above.",
                left, introY + 30f, bodyFont, softPaint);
        }

        // ---- the actionable table (mockup's LINE / LAST MONTH / WROTE / SUGGEST / FINAL) ----
        var tableHeaderBaseline = FollowUpRegions.TableTop - FollowUpRegions.TableHeaderHeight + PaperTypeScale.Label;
        canvas.DrawText("LINE", left, tableHeaderBaseline, labelFont, mutedPaint);
        canvas.DrawText("LAST MONTH", left + 190f, tableHeaderBaseline, labelFont, mutedPaint);
        canvas.DrawText("WROTE", left + 280f, tableHeaderBaseline, labelFont, mutedPaint);
        canvas.DrawText("SUGGEST", left + 360f, tableHeaderBaseline, labelFont, mutedPaint);
        canvas.DrawText("FINAL", right - FollowUpRegions.BoxWidth, tableHeaderBaseline, labelFont, mutedPaint);

        var boxes = FollowUpRegions.ActionableBoxes(plan.ActionableKeysInPrintedOrder);
        for (var i = 0; i < plan.ActionableLines.Count; i++)
        {
            var line = plan.ActionableLines[i];
            var box = boxes[i];
            var baseline = box.Top + box.Height / 2 + 4f;

            canvas.DrawText(line.Name, left, baseline, bodyFont, inkPaint);
            if (!string.IsNullOrEmpty(line.Detail))
            {
                canvas.DrawText(line.Detail, left, baseline + 12f, secondaryFont, mutedPaint);
            }
            canvas.DrawText(Money(line.LastMonthWholeDollars), left + 190f, baseline, monoFont, softPaint);
            // A figure the suggestion changes reads struck through on the page — approximated here as
            // parentheses around the written figure (the vector text a reader extracts either way; the
            // strike itself is a drawn line, added below only when the figure actually changes).
            //
            // A line whose limit could not be read has no written figure to show: saying "$0" would be
            // claiming a reading that was never made, so the column says what happened instead.
            if (line.WasUnreadable)
            {
                canvas.DrawText(UnreadMark, left + 280f, baseline, secondaryFont, mutedPaint);
            }
            else
            {
                canvas.DrawText(Money(line.WrittenWholeDollars), left + 280f, baseline, monoFont, line.IsSuggested ? mutedPaint : softPaint);
            }
            if (line.IsSuggested)
            {
                var writtenWidth = monoFont.MeasureText(Money(line.WrittenWholeDollars));
                canvas.DrawLine(left + 280f, baseline - 4f, left + 280f + writtenWidth, baseline - 4f, mutedPaint);
            }
            canvas.DrawText(line.IsSuggested ? Money(line.SuggestedWholeDollars!.Value) : "—", left + 360f, baseline, monoFont, inkPaint);

            DrawBox(canvas, boxPaint, box);
            canvas.DrawText("$", box.Left + 6f, box.Top + box.Height / 2 + 4f, monoFont, mutedPaint);
        }

        // ---- everything below the table is placed by FollowUpRegions.Layout, the one sum that also
        // positions the "change another line" boxes (Guard R-G12) ----
        var layout = FollowUpRegions.Layout(plan.ActionableLines.Count, plan.ReferenceLines.Count);

        // "With the suggestion(s)" — a page that only asks for unread limits suggests nothing, so it has
        // no such line (the space it would take stays, so the layout is the one FollowUpRegions decides).
        if (!plan.IsResendOnly)
        {
            var suggestionsWord = plan.ActionableLines.Count(l => !l.WasUnreadable) == 1 ? "suggestion" : "suggestions";
            canvas.DrawText($"With the {suggestionsWord}", left, layout.WithSuggestionsBaseline, labelFont, inkPaint);
            var balancedText = plan.WithSuggestionsBalances ? $"{Money(plan.TargetWholeDollars)} · balanced" : Money(plan.TargetWholeDollars);
            canvas.DrawText(balancedText, right, layout.WithSuggestionsBaseline, SKTextAlign.Right, monoFont, inkPaint);
        }

        // "Everything else, as written" — two columns, each line labelled for what it is (a savings line
        // by its account or goal, mockup 13(b)'s "Emergency fund" / "Summer trip", not "Savings" twice)
        if (layout.ReferenceHeaderBaseline is { } referenceHeader)
        {
            canvas.DrawText("EVERYTHING ELSE, AS WRITTEN", left, referenceHeader, labelFont, mutedPaint);
            for (var i = 0; i < plan.ReferenceLines.Count; i++)
            {
                var line = plan.ReferenceLines[i];
                var column = i % FollowUpRegions.ReferenceColumns;
                var row = i / FollowUpRegions.ReferenceColumns;
                var x = column == 0 ? left : left + FollowUpRegions.BodyWidth / 2;
                var y = layout.ReferenceFirstRowBaseline + row * FollowUpRegions.ReferenceRowPitch;
                canvas.DrawText(line.ReferenceLabel, x, y, secondaryFont, inkPaint);
                canvas.DrawText(Money(line.WrittenWholeDollars), x + FollowUpRegions.BodyWidth / 2 - 24f, y, SKTextAlign.Right, monoFont, inkPaint);
            }
        }

        // "Change another line" — label, then the boxes it labels, then the sentence under them
        canvas.DrawText("CHANGE ANOTHER LINE", left, layout.ChangeHeaderBaseline, labelFont, mutedPaint);

        var changeBoxes = FollowUpRegions.ChangeAnotherLineBoxes(plan.ActionableLines.Count, plan.ReferenceLines.Count);
        foreach (var (nameBox, amountBox) in changeBoxes)
        {
            DrawBox(canvas, boxPaint, nameBox);
            DrawBox(canvas, boxPaint, amountBox);
            canvas.DrawText("$", amountBox.Left + 6f, amountBox.Top + amountBox.Height / 2 + 4f, monoFont, mutedPaint);
        }

        var footerSentence = plan.IsResendOnly
            ? "If a limit still can't be read, another page like this comes tomorrow."
            : plan.HasResend
                ? $"If the final figures still don't come to {Money(plan.TargetWholeDollars)}, or a limit can't be read, another page like this comes tomorrow."
                : $"If the final figures still don't come to {Money(plan.TargetWholeDollars)}, another page like this comes tomorrow.";
        canvas.DrawText(footerSentence, left, layout.FooterSentenceBaseline, secondaryFont, softPaint);

        // ---- footer ----
        var footerY = FollowUpRegions.PageHeight - FollowUpRegions.MarginBottom + 18f;
        canvas.DrawText($"Index · {monthDisplayName} follow-up", left, footerY, secondaryFont, mutedPaint);
        canvas.DrawText($"printed {plan.PrintedOn:MMM d}", right, footerY, SKTextAlign.Right, secondaryFont, mutedPaint);
    }

    private static void DrawBox(SKCanvas canvas, SKPaint paint, WorksheetBox box) =>
        canvas.DrawRoundRect(new SKRect(box.Left, box.Top, box.Left + box.Width, box.Top + box.Height), 3f, 3f, paint);

    /// <summary>The small heading at the top of the page. One source for the painter and for the read-back
    /// instruction, which tells the reader to refuse a page whose heading names another month.</summary>
    public static string Heading(string yearMonth) => $"INDEX · {MonthDisplayName(yearMonth).ToUpperInvariant()} · FOLLOW-UP";

    /// <summary>What the "wrote" column says for a limit that was never read (see the row drawing).</summary>
    internal const string UnreadMark = "not read";

    private static string ResendCountPhrase(int count) => count == 1 ? "1 limit" : $"{count} limits";

    private static string Money(long wholeDollars) => $"${wholeDollars:N0}";

    private static string MonthDisplayName(string yearMonth)
    {
        var parsed = DateOnly.ParseExact(yearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return parsed.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }
}
