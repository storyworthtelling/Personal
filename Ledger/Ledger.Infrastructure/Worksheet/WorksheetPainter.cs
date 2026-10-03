using SkiaSharp;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Storyworthtelling.Paper;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// Draws the monthly worksheet (mockup 12/13, Release F/H) through <see cref="VectorPdfSink"/> — real
/// vector text, not a rasterised picture of a page — in the Document (muted) colour profile and
/// <see cref="PaperTypeScale"/>, per the plan's own rule that printed pages never use the lively "App"
/// profile. Index's identity hue is recorded at 0&#176; from the Positive role (D115/D116), so the
/// header rule below draws <see cref="PaperColorTokens.PositiveDoc"/> as the identity mark, not as a
/// page-level Positive-role usage — this is the one exception the design book itself records, not a
/// second one invented here.
/// </summary>
public static class WorksheetPainter
{
    /// <summary>Every box this plan's page carries, computed the one way <see cref="WorksheetRegions"/>
    /// computes them — <see cref="Paint"/> draws exactly this list, so a test can call this directly to
    /// prove the two agree (Guard G18) without parsing PDF drawing operators back out of the bytes.</summary>
    public static IReadOnlyList<WorksheetBox> BoxesFor(Domain.Worksheet.WorksheetPlan plan) =>
        WorksheetRegions.AllBoxes(plan.CategoryIdsInPrintedOrder, plan.SetAsideKeysInPrintedOrder);

    public static byte[] Paint(Domain.Worksheet.WorksheetPlan plan)
    {
        using var sink = new VectorPdfSink();
        using var ink = new PageInk();

        // One layout answers where every box is on every sheet, and this method draws exactly that —
        // the read-back prompt is told the same list (Guard G18, two-page form).
        var layout = WorksheetRegions.Layout(plan.CategoryIdsInPrintedOrder, plan.SetAsideKeysInPrintedOrder);
        var split = layout.Split;

        var page1Categories = plan.Categories.Take(split.CategoryRowsOnPageOne).ToList();
        var canvas = sink.BeginPage(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);
        DrawPageOne(canvas, ink, plan, page1Categories, layout);
        sink.EndPage();

        if (split.NeedsSecondPage)
        {
            var page2Categories = plan.Categories.Skip(split.CategoryRowsOnPageOne).ToList();
            var canvas2 = sink.BeginPage(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);
            DrawPageTwo(canvas2, ink, plan, page2Categories, layout);
            sink.EndPage();
        }

        return sink.Close();
    }

    private static void DrawPageOne(
        SKCanvas canvas, PageInk ink, Domain.Worksheet.WorksheetPlan plan,
        IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> page1Categories, WorksheetPageLayout layout)
    {
        var drawTrailingSections = !layout.Split.NeedsSecondPage;
        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;
        var monthYear = WorksheetDates.MonthYear(plan.TargetYearMonth);
        var monthWord = WorksheetDates.MonthWord(plan.TargetYearMonth);
        var previousMonth = WorksheetDates.PreviousMonthWord(plan.TargetYearMonth);

        var ruleY = DrawHeader(canvas, ink, "INDEX · HOUSEHOLD BUDGET", monthYear);

        // ---- intro (D100/D155: states the zero-based target when this plan carries a Set aside
        // section, matching mockup 13(a)'s own wording; Release F's plain intro otherwise) ----
        var introBaseline = ruleY + IntroOffset;
        if (plan.PrintedTargetWholeDollars is { } target)
        {
            ink.Paragraph(
                canvas,
                [
                    ($"Write each line's limit in its box; an empty box keeps {previousMonth}'s figure. Every dollar gets a job: the boxes should come to ", false),
                    (PlanMoney.Whole(target), true),
                    (", and Index checks when it reads the page.", false),
                ],
                left, introBaseline, WorksheetRegions.BodyWidth, IntroLineHeight);
        }
        else
        {
            ink.Paragraph(
                canvas,
                [($"Agree a limit for each line together and write it in its box; an empty box keeps {previousMonth}'s figure. Add a category on a blank line at the foot.", false)],
                left, introBaseline, WorksheetRegions.BodyWidth, IntroLineHeight);
        }

        // ---- income block (D155) ----
        var calcTop = WorksheetRegions.IncomeBlockTop + PaperTypeScale.Secondary;
        var incomeLine = plan.IncomeConfident
            ? PlanMoney.Whole(plan.IncomeWholeDollars)
            : Domain.Worksheet.WorksheetPlan.IncomeUnsureText;
        DrawCalcRow(canvas, ink, left, calcTop, $"{monthWord} income, expected", incomeLine, ink.Ink, ink.Soft);
        DrawCalcRow(canvas, ink, left, calcTop + 14f, $"Snowball payments, {FridayWord(plan.FridayCount)}", PlanMoney.Deduction(plan.SnowballPaymentsWholeDollars), ink.Soft, ink.Soft);
        DrawCalcRow(canvas, ink, left, calcTop + 28f, $"{previousMonth}'s spending", PlanMoney.Deduction(plan.LastMonthSpendingWholeDollars), ink.Soft, ink.Soft);
        if (plan.LeftToAssignWholeDollars is { } leftToAssign)
        {
            // .ws-calc .r.tot — a 1pt ink rule across the calc block, then the total.
            PageInk.Rule(canvas, ink.InkHairRule, left, left + WorksheetRegions.CalcColumnWidth, calcTop + 28f + 5f);
            DrawCalcRow(canvas, ink, left, calcTop + 46f, "Left to assign", PlanMoney.Whole(leftToAssign), ink.Ink, ink.Ink);
        }

        var incomeBox = WorksheetRegions.IncomeCorrectionBox();
        canvas.DrawText("INCOME DIFFERENT?", incomeBox.Left, incomeBox.Top - 4f, ink.Label, ink.Muted);
        DrawBox(canvas, ink.Box, incomeBox);
        canvas.DrawText("$", incomeBox.Left + 6f, incomeBox.Top + incomeBox.Height / 2 + 4f, ink.Mono, ink.Muted);

        // ---- category header (mockup 12's ".ws-head") ----
        DrawCategoryHeader(canvas, ink, WorksheetRegions.CategoryHeaderTop + PaperTypeScale.Label, WorksheetRegions.CategoryHeaderTop + WorksheetRegions.SectionHeaderHeight, plan.TargetYearMonth);

        // ---- one row per category on THIS page (D151) ----
        var categoryBoxes = layout.Categories.Where(b => b.Page == 0).ToList();
        DrawCategoryRows(canvas, ink, page1Categories, categoryBoxes);

        if (drawTrailingSections)
        {
            DrawSetAsideAndWriteIns(canvas, ink, plan.SetAsideLinesOrEmpty, layout);
        }
        else
        {
            var continuedY = WorksheetRegions.AddCategoryHeaderTop(page1Categories.Count) + PaperTypeScale.Secondary;
            canvas.DrawText("Continued on the next sheet…", left, continuedY, ink.Secondary, ink.Muted);
        }

        DrawFooter(canvas, ink, monthYear, plan.PrintedOn, left, right);
    }

    private static void DrawPageTwo(
        SKCanvas canvas, PageInk ink, Domain.Worksheet.WorksheetPlan plan,
        IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> page2Categories, WorksheetPageLayout layout)
    {
        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;
        var monthYear = WorksheetDates.MonthYear(plan.TargetYearMonth);

        DrawHeader(canvas, ink, "INDEX · HOUSEHOLD BUDGET · CONTINUED", monthYear);

        // A continuation sheet that carries no categories (they all fitted on page one; only the
        // set-aside unit moved) has nothing to introduce and no table to head — an empty CATEGORY /
        // previous-month header over no rows would be a table of nothing.
        if (page2Categories.Count > 0)
        {
            canvas.DrawText("The rest of this month's categories, continued from the previous sheet.", left, WorksheetRegions.ContinuationIntroBaseline, ink.Body, ink.Soft);

            DrawCategoryHeader(
                canvas, ink, WorksheetRegions.ContinuationHeaderBaseline,
                WorksheetRegions.ContinuationRowsTop(page2Categories.Count) - PageInk.HeavyRule / 2, plan.TargetYearMonth);

            var categoryBoxes = layout.Categories.Where(b => b.Page == 1).ToList();
            DrawCategoryRows(canvas, ink, page2Categories, categoryBoxes);
        }

        DrawSetAsideAndWriteIns(canvas, ink, plan.SetAsideLinesOrEmpty, layout);

        DrawFooter(canvas, ink, monthYear, plan.PrintedOn, left, right);
    }

    /// <summary>The kicker, the title at <see cref="PaperTypeScale.Display"/> and the 1.5pt identity rule
    /// under them (<c>.rp-lab</c>, <c>.rp-title</c>, <c>.rp-rule</c>). Returns the rule's y.</summary>
    private static float DrawHeader(SKCanvas canvas, PageInk ink, string kicker, string title)
    {
        var left = WorksheetRegions.MarginLeft;
        canvas.DrawText(kicker, left, WorksheetRegions.MarginTop + PaperTypeScale.Label, ink.Label, ink.Muted);
        canvas.DrawText(title, left, WorksheetRegions.MarginTop + PaperTypeScale.Label + PaperTypeScale.Display + TitleDrop, ink.Title, ink.Ink);
        var ruleY = WorksheetRegions.MarginTop + WorksheetRegions.HeaderHeight - 8f;
        PageInk.Rule(canvas, ink.IdentityRule, left, left + WorksheetRegions.BodyWidth, ruleY);
        return ruleY;
    }

    /// <summary>Gap from the label's baseline plus the title's size to the title's baseline: the title's
    /// line box (33pt at 1.06) sits 4pt under the label's, and its baseline falls near the box's foot.</summary>
    private const float TitleDrop = 5f;

    /// <summary>From the header rule to the first intro baseline (14pt margin + the line's own ascent) and
    /// the paragraph's line height (<c>.rp-b</c>: 12px at 1.4).</summary>
    private const float IntroOffset = 25f;
    private const float IntroLineHeight = 16.8f;

    /// <summary>CATEGORY, the previous month's name (right-aligned over its figures) and "&lt;MONTH&gt; LIMIT",
    /// with the 1.5pt ink rule under them (<c>.ws-head</c>).</summary>
    private static void DrawCategoryHeader(SKCanvas canvas, PageInk ink, float labelBaseline, float ruleY, string yearMonth)
    {
        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;
        canvas.DrawText("CATEGORY", left, labelBaseline, ink.Label, ink.Muted);
        canvas.DrawText(WorksheetDates.PreviousMonthWord(yearMonth).ToUpperInvariant(), WorksheetRegions.LastMonthColumnRight, labelBaseline, SKTextAlign.Right, ink.Label, ink.Muted);
        canvas.DrawText($"{WorksheetDates.MonthWord(yearMonth).ToUpperInvariant()} LIMIT", right, labelBaseline, SKTextAlign.Right, ink.Label, ink.Muted);
        PageInk.Rule(canvas, ink.InkRule, left, right, ruleY);
    }

    private static void DrawCategoryRows(
        SKCanvas canvas, PageInk ink, IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> categories, IReadOnlyList<WorksheetBox> categoryBoxes)
    {
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            var drawnBox = categoryBoxes[i];
            // The row sits where its box does (a box is centred in its row), so the text can never
            // drift from the box the read-back is told about.
            var rowTop = drawnBox.Top - (WorksheetRegions.RowHeight - WorksheetRegions.BoxHeight) / 2;
            var baseline = rowTop + WorksheetRegions.RowHeight / 2 + PaperTypeScale.Body / 2;

            canvas.DrawText(category.Name, WorksheetRegions.MarginLeft, baseline, ink.Body, ink.Ink);
            canvas.DrawText(
                PlanMoney.Whole(WholeDollarsFloor(category.LastMonthActual)),
                WorksheetRegions.LastMonthColumnRight, baseline, SKTextAlign.Right, ink.Mono, ink.Ink);

            DrawBox(canvas, ink.Box, drawnBox);
            canvas.DrawText("$", drawnBox.Left + 6f, drawnBox.Top + drawnBox.Height / 2 + 4f, ink.Mono, ink.Muted);
            DrawRowRule(canvas, ink, rowTop);
        }
    }

    /// <summary>The hairline closing a row whose top is <paramref name="rowTop"/> (<c>.ws-row</c>'s 1px
    /// bottom border, which is the last pixel of the row's pitch).</summary>
    private static void DrawRowRule(SKCanvas canvas, PageInk ink, float rowTop) =>
        PageInk.Rule(
            canvas, ink.RowRule, WorksheetRegions.MarginLeft, WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth,
            rowTop + WorksheetRegions.RowHeight + PageInk.LightRule / 2);

    /// <summary>D98/D99: the "Set aside" section (when this plan carries one) plus the write-in lines, on
    /// whichever sheet <see cref="WorksheetPageLayout.SetAsidePage"/> says they are — drawn from the
    /// layout's own boxes and section tops, so page one and the continuation sheet share one routine.</summary>
    private static void DrawSetAsideAndWriteIns(
        SKCanvas canvas, PageInk ink, IReadOnlyList<WorksheetSetAsideLine> setAsideLines, WorksheetPageLayout layout)
    {
        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;

        if (setAsideLines.Count > 0)
        {
            DrawSectionHeader(canvas, ink, "SET ASIDE", layout.SetAsideHeaderTop);

            for (var i = 0; i < setAsideLines.Count; i++)
            {
                var line = setAsideLines[i];
                var box = layout.SetAside[i];
                var titleBaseline = box.Top + PaperTypeScale.Body - 2f;
                canvas.DrawText(line.Title, left, titleBaseline, ink.Body, ink.Ink);
                canvas.DrawText(line.Detail, left, titleBaseline + 12f, ink.Secondary, ink.Muted);
                canvas.DrawText(
                    PlanMoney.Whole(WholeDollarsFloor(line.LastMonthActual)),
                    WorksheetRegions.LastMonthColumnRight, titleBaseline, SKTextAlign.Right, ink.Mono, ink.Ink);
                DrawBox(canvas, ink.Box, box);
                canvas.DrawText("$", box.Left + 6f, box.Top + box.Height / 2 + 4f, ink.Mono, ink.Muted);
                DrawRowRule(canvas, ink, box.Top - (WorksheetRegions.RowHeight - WorksheetRegions.BoxHeight) / 2);
            }
        }

        DrawSectionHeader(canvas, ink, "ADD A CATEGORY", layout.AddCategoryHeaderTop);

        foreach (var (nameBox, limitBox) in layout.NewCategories)
        {
            DrawBox(canvas, ink.Box, nameBox);
            DrawBox(canvas, ink.Box, limitBox);
            canvas.DrawText("$", limitBox.Left + 6f, limitBox.Top + limitBox.Height / 2 + 4f, ink.Mono, ink.Muted);
            DrawRowRule(canvas, ink, limitBox.Top - (WorksheetRegions.RowHeight - WorksheetRegions.BoxHeight) / 2);
        }
    }

    /// <summary>A section label with its 1.5pt ink rule (<c>.ws-sub</c>), the rule on the band's lower edge
    /// so the first row starts right under it.</summary>
    private static void DrawSectionHeader(SKCanvas canvas, PageInk ink, string label, float top)
    {
        canvas.DrawText(label, WorksheetRegions.MarginLeft, top + PaperTypeScale.Label, ink.Label, ink.Muted);
        PageInk.Rule(
            canvas, ink.InkRule, WorksheetRegions.MarginLeft, WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth,
            top + WorksheetRegions.SectionHeaderHeight - PageInk.HeavyRule / 2);
    }

    private static void DrawFooter(SKCanvas canvas, PageInk ink, string monthYear, DateOnly printedOn, float left, float right)
    {
        var footerY = WorksheetRegions.PageHeight - WorksheetRegions.MarginBottom + 18f;
        canvas.DrawText($"Index · {monthYear} budget", left, footerY, ink.Secondary, ink.Muted);
        // .folio .r — the date is mono and sits against the right margin.
        canvas.DrawText($"printed {WorksheetDates.PrintedOn(printedOn)}", right, footerY, SKTextAlign.Right, ink.Mono, ink.Muted);
    }

    private static void DrawCalcRow(
        SKCanvas canvas, PageInk ink, float left, float y, string label, string value, SKPaint valuePaint, SKPaint labelPaint)
    {
        canvas.DrawText(label, left, y, ink.Secondary, labelPaint);
        canvas.DrawText(value, left + WorksheetRegions.CalcColumnWidth, y, SKTextAlign.Right, ink.Mono, valuePaint);
    }

    private static void DrawBox(SKCanvas canvas, SKPaint paint, WorksheetBox box) =>
        canvas.DrawRoundRect(new SKRect(box.Left, box.Top, box.Left + box.Width, box.Top + box.Height), 3f, 3f, paint);

    private static long WholeDollarsFloor(decimal amount) => Index.Domain.Worksheet.WholeDollars.Floor(amount);

    private static string FridayWord(int fridayCount) => fridayCount == 1 ? "1 Friday" : $"{fridayCount} Fridays";
}
