using System.Globalization;
using SkiaSharp;
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

        // One layout answers where every box is on every sheet, and this method draws exactly that —
        // the read-back prompt is told the same list (Guard G18, two-page form).
        var layout = WorksheetRegions.Layout(plan.CategoryIdsInPrintedOrder, plan.SetAsideKeysInPrintedOrder);
        var split = layout.Split;

        var page1Categories = plan.Categories.Take(split.CategoryRowsOnPageOne).ToList();
        var canvas = sink.BeginPage(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);
        DrawPageOne(canvas, plan, page1Categories, layout);
        sink.EndPage();

        if (split.NeedsSecondPage)
        {
            var page2Categories = plan.Categories.Skip(split.CategoryRowsOnPageOne).ToList();
            var canvas2 = sink.BeginPage(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);
            DrawPageTwo(canvas2, plan, page2Categories, layout);
            sink.EndPage();
        }

        return sink.Close();
    }

    private static void DrawPageOne(
        SKCanvas canvas, Domain.Worksheet.WorksheetPlan plan, IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> page1Categories,
        WorksheetPageLayout layout)
    {
        var drawTrailingSections = !layout.Split.NeedsSecondPage;
        var (ink, inkSoft, inkMuted, identityRule, boxStroke) = Colors();
        var (labelFont, titleFont, bodyFont, secondaryFont, monoFont) = Fonts();
        var (inkPaint, softPaint, mutedPaint, rulePaint, boxPaint) = Paints(ink, inkSoft, inkMuted, identityRule, boxStroke);

        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;
        var monthDisplayName = MonthDisplayName(plan.TargetYearMonth);

        // ---- header ----
        canvas.DrawText("INDEX · HOUSEHOLD BUDGET", left, WorksheetRegions.MarginTop + PaperTypeScale.Label, labelFont, mutedPaint);
        canvas.DrawText(monthDisplayName, left, WorksheetRegions.MarginTop + PaperTypeScale.Label + PaperTypeScale.Title + 2f, titleFont, inkPaint);
        var ruleY = WorksheetRegions.MarginTop + WorksheetRegions.HeaderHeight - 8f;
        canvas.DrawLine(left, ruleY, right, ruleY, rulePaint);

        // ---- intro (D100/D155: states the zero-based target when this plan carries a Set aside
        // section, matching mockup 13(a)'s own wording; Release F's plain intro otherwise) ----
        var introY = ruleY + 16f;
        if (plan.PrintedTargetWholeDollars is { } target)
        {
            canvas.DrawText(
                "Write each line's limit in its box; an empty box keeps last month's figure.",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                $"Every dollar gets a job: the boxes should come to {Money(target)}, and Index checks when it reads the page.",
                left, introY + 15f, bodyFont, softPaint);
        }
        else
        {
            canvas.DrawText(
                "Agree a limit for each line together and write it in its box; an empty box keeps last",
                left, introY, bodyFont, softPaint);
            canvas.DrawText(
                "month's figure. Add a category on a blank line at the foot.",
                left, introY + 15f, bodyFont, softPaint);
        }

        // ---- income block (D155) ----
        var calcTop = WorksheetRegions.IncomeBlockTop + PaperTypeScale.Secondary;
        var incomeLine = plan.IncomeConfident
            ? Money(plan.IncomeWholeDollars)
            : Domain.Worksheet.WorksheetPlan.IncomeUnsureText;
        DrawCalcRow(canvas, left, calcTop, $"{monthDisplayName.Split(' ')[0]} income, expected", incomeLine, monoFont, secondaryFont, inkPaint, softPaint);
        DrawCalcRow(canvas, left, calcTop + 14f, $"Snowball payments, {FridayWord(plan.FridayCount)}", $"−{Money(plan.SnowballPaymentsWholeDollars)}", monoFont, secondaryFont, softPaint, softPaint);
        DrawCalcRow(canvas, left, calcTop + 28f, "Last month's spending", $"−{Money(plan.LastMonthSpendingWholeDollars)}", monoFont, secondaryFont, softPaint, softPaint);
        if (plan.LeftToAssignWholeDollars is { } leftToAssign)
        {
            DrawCalcRow(canvas, left, calcTop + 46f, "Left to assign", Money(leftToAssign), monoFont, secondaryFont, inkPaint, inkPaint);
        }

        var incomeBox = WorksheetRegions.IncomeCorrectionBox();
        canvas.DrawText("INCOME DIFFERENT?", incomeBox.Left, incomeBox.Top - 4f, labelFont, mutedPaint);
        DrawBox(canvas, boxPaint, incomeBox);
        canvas.DrawText("$", incomeBox.Left + 6f, incomeBox.Top + incomeBox.Height / 2 + 4f, monoFont, mutedPaint);

        // ---- category header (mockup 12's ".ws-head") ----
        var headerBaseline = WorksheetRegions.CategoryHeaderTop + PaperTypeScale.Label;
        canvas.DrawText("CATEGORY", left, headerBaseline, labelFont, mutedPaint);
        canvas.DrawText("LAST MONTH", left + WorksheetRegions.NameColumnWidth, headerBaseline, SKTextAlign.Right, labelFont, mutedPaint);
        var thisMonthLabel = $"{monthDisplayName.Split(' ')[0].ToUpperInvariant()} LIMIT";
        canvas.DrawText(thisMonthLabel, right, headerBaseline, SKTextAlign.Right, labelFont, mutedPaint);

        // ---- one row per category on THIS page (D151) ----
        var categoryBoxes = layout.Categories.Where(b => b.Page == 0).ToList();
        DrawCategoryRows(canvas, page1Categories, categoryBoxes, bodyFont, monoFont, mutedPaint, inkPaint, boxPaint);

        if (drawTrailingSections)
        {
            DrawSetAsideAndWriteIns(
                canvas, plan.SetAsideLinesOrEmpty, layout,
                labelFont, bodyFont, secondaryFont, monoFont, inkPaint, softPaint, mutedPaint, boxPaint);
        }
        else
        {
            var continuedY = WorksheetRegions.AddCategoryHeaderTop(page1Categories.Count) + PaperTypeScale.Secondary;
            canvas.DrawText("Continued on the next sheet…", left, continuedY, secondaryFont, mutedPaint);
        }

        DrawFooter(canvas, monthDisplayName, plan.PrintedOn, left, right, secondaryFont, mutedPaint);
    }

    private static void DrawPageTwo(
        SKCanvas canvas, Domain.Worksheet.WorksheetPlan plan, IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> page2Categories,
        WorksheetPageLayout layout)
    {
        var (ink, inkSoft, inkMuted, identityRule, boxStroke) = Colors();
        var (labelFont, titleFont, bodyFont, secondaryFont, monoFont) = Fonts();
        var (inkPaint, softPaint, mutedPaint, rulePaint, boxPaint) = Paints(ink, inkSoft, inkMuted, identityRule, boxStroke);

        var left = WorksheetRegions.MarginLeft;
        var right = left + WorksheetRegions.BodyWidth;
        var monthDisplayName = MonthDisplayName(plan.TargetYearMonth);

        canvas.DrawText("INDEX · HOUSEHOLD BUDGET · CONTINUED", left, WorksheetRegions.MarginTop + PaperTypeScale.Label, labelFont, mutedPaint);
        canvas.DrawText(monthDisplayName, left, WorksheetRegions.MarginTop + PaperTypeScale.Label + PaperTypeScale.Title + 2f, titleFont, inkPaint);
        var ruleY = WorksheetRegions.MarginTop + WorksheetRegions.HeaderHeight - 8f;
        canvas.DrawLine(left, ruleY, right, ruleY, rulePaint);

        // A continuation sheet that carries no categories (they all fitted on page one; only the
        // set-aside unit moved) has nothing to introduce and no table to head — an empty CATEGORY /
        // LAST MONTH header over no rows would be a table of nothing.
        if (page2Categories.Count > 0)
        {
            canvas.DrawText("The rest of this month's categories, continued from the previous sheet.", left, WorksheetRegions.ContinuationIntroBaseline, bodyFont, softPaint);

            var headerBaseline = WorksheetRegions.ContinuationHeaderBaseline;
            canvas.DrawText("CATEGORY", left, headerBaseline, labelFont, mutedPaint);
            canvas.DrawText("LAST MONTH", left + WorksheetRegions.NameColumnWidth, headerBaseline, SKTextAlign.Right, labelFont, mutedPaint);
            var thisMonthLabel = $"{monthDisplayName.Split(' ')[0].ToUpperInvariant()} LIMIT";
            canvas.DrawText(thisMonthLabel, right, headerBaseline, SKTextAlign.Right, labelFont, mutedPaint);

            var categoryBoxes = layout.Categories.Where(b => b.Page == 1).ToList();
            DrawCategoryRows(canvas, page2Categories, categoryBoxes, bodyFont, monoFont, mutedPaint, inkPaint, boxPaint);
        }

        DrawSetAsideAndWriteIns(
            canvas, plan.SetAsideLinesOrEmpty, layout,
            labelFont, bodyFont, secondaryFont, monoFont, inkPaint, softPaint, mutedPaint, boxPaint);

        DrawFooter(canvas, monthDisplayName, plan.PrintedOn, left, right, secondaryFont, mutedPaint);
    }

    private static void DrawCategoryRows(
        SKCanvas canvas, IReadOnlyList<Domain.Worksheet.WorksheetCategoryLine> categories, IReadOnlyList<WorksheetBox> categoryBoxes,
        SKFont bodyFont, SKFont monoFont, SKPaint mutedPaint, SKPaint inkPaint, SKPaint boxPaint)
    {
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            var drawnBox = categoryBoxes[i];
            // The row sits where its box does (a box is centred in its row), so the text can never
            // drift from the box the read-back is told about.
            var rowTop = drawnBox.Top - (WorksheetRegions.RowHeight - WorksheetRegions.BoxHeight) / 2;
            var baseline = rowTop + WorksheetRegions.RowHeight / 2 + PaperTypeScale.Body / 2;

            canvas.DrawText(category.Name, WorksheetRegions.MarginLeft, baseline, bodyFont, inkPaint);
            canvas.DrawText(
                Money(WholeDollarsFloor(category.LastMonthActual)),
                WorksheetRegions.MarginLeft + WorksheetRegions.NameColumnWidth, baseline, SKTextAlign.Right, monoFont, inkPaint);

            DrawBox(canvas, boxPaint, drawnBox);
            canvas.DrawText("$", drawnBox.Left + 6f, drawnBox.Top + drawnBox.Height / 2 + 4f, monoFont, mutedPaint);
        }
    }

    /// <summary>D98/D99: the "Set aside" section (when this plan carries one) plus the write-in lines, on
    /// whichever sheet <see cref="WorksheetPageLayout.SetAsidePage"/> says they are — drawn from the
    /// layout's own boxes and section tops, so page one and the continuation sheet share one routine.</summary>
    private static void DrawSetAsideAndWriteIns(
        SKCanvas canvas, IReadOnlyList<WorksheetSetAsideLine> setAsideLines, WorksheetPageLayout layout,
        SKFont labelFont, SKFont bodyFont, SKFont secondaryFont, SKFont monoFont,
        SKPaint inkPaint, SKPaint softPaint, SKPaint mutedPaint, SKPaint boxPaint)
    {
        var left = WorksheetRegions.MarginLeft;

        if (setAsideLines.Count > 0)
        {
            canvas.DrawText("SET ASIDE", left, layout.SetAsideHeaderTop + PaperTypeScale.Label, labelFont, mutedPaint);

            for (var i = 0; i < setAsideLines.Count; i++)
            {
                var line = setAsideLines[i];
                var box = layout.SetAside[i];
                var titleBaseline = box.Top + PaperTypeScale.Body - 2f;
                canvas.DrawText(line.Title, left, titleBaseline, bodyFont, inkPaint);
                canvas.DrawText(line.Detail, left, titleBaseline + 12f, secondaryFont, mutedPaint);
                canvas.DrawText(
                    Money(WholeDollarsFloor(line.LastMonthActual)),
                    left + WorksheetRegions.NameColumnWidth, titleBaseline, SKTextAlign.Right, monoFont, inkPaint);
                DrawBox(canvas, boxPaint, box);
                canvas.DrawText("$", box.Left + 6f, box.Top + box.Height / 2 + 4f, monoFont, mutedPaint);
            }
        }

        canvas.DrawText("ADD A CATEGORY", left, layout.AddCategoryHeaderTop + PaperTypeScale.Label, labelFont, mutedPaint);

        foreach (var (nameBox, limitBox) in layout.NewCategories)
        {
            DrawBox(canvas, boxPaint, nameBox);
            DrawBox(canvas, boxPaint, limitBox);
            canvas.DrawText("$", limitBox.Left + 6f, limitBox.Top + limitBox.Height / 2 + 4f, monoFont, mutedPaint);
        }
    }

    private static void DrawFooter(
        SKCanvas canvas, string monthDisplayName, DateOnly printedOn, float left, float right, SKFont secondaryFont, SKPaint mutedPaint)
    {
        var footerY = WorksheetRegions.PageHeight - WorksheetRegions.MarginBottom + 18f;
        canvas.DrawText($"Index · {monthDisplayName} budget", left, footerY, secondaryFont, mutedPaint);
        canvas.DrawText($"printed {printedOn:MMM d}", right, footerY, SKTextAlign.Right, secondaryFont, mutedPaint);
    }

    private static (SKColor Ink, SKColor InkSoft, SKColor InkMuted, SKColor IdentityRule, SKColor BoxStroke) Colors() => (
        SKColor.Parse(PaperColorTokens.Ink), SKColor.Parse(PaperColorTokens.InkSoft), SKColor.Parse(PaperColorTokens.InkMuted),
        SKColor.Parse(PaperColorTokens.PositiveDoc), SKColor.Parse(PaperColorTokens.InkMuted));

    private static (SKFont Label, SKFont Title, SKFont Body, SKFont Secondary, SKFont Mono) Fonts() => (
        new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label),
        new SKFont(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Title),
        new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body),
        new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary),
        new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Mono));

    private static (SKPaint Ink, SKPaint Soft, SKPaint Muted, SKPaint Rule, SKPaint Box) Paints(
        SKColor ink, SKColor inkSoft, SKColor inkMuted, SKColor identityRule, SKColor boxStroke) => (
        new SKPaint { Color = ink, IsAntialias = true },
        new SKPaint { Color = inkSoft, IsAntialias = true },
        new SKPaint { Color = inkMuted, IsAntialias = true },
        new SKPaint { Color = identityRule, IsAntialias = true },
        new SKPaint { Color = boxStroke, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f });

    private static void DrawCalcRow(
        SKCanvas canvas, float left, float y, string label, string value,
        SKFont valueFont, SKFont labelFont, SKPaint valuePaint, SKPaint labelPaint)
    {
        canvas.DrawText(label, left, y, labelFont, labelPaint);
        canvas.DrawText(value, left + 260f, y, SKTextAlign.Right, valueFont, valuePaint);
    }

    private static void DrawBox(SKCanvas canvas, SKPaint paint, WorksheetBox box) =>
        canvas.DrawRoundRect(new SKRect(box.Left, box.Top, box.Left + box.Width, box.Top + box.Height), 3f, 3f, paint);

    private static long WholeDollarsFloor(decimal amount) => Index.Domain.Worksheet.WholeDollars.Floor(amount);

    private static string Money(long wholeDollars) => $"${wholeDollars:N0}";

    private static string FridayWord(int fridayCount) => fridayCount == 1 ? "1 Friday" : $"{fridayCount} Fridays";

    private static string MonthDisplayName(string yearMonth)
    {
        var parsed = DateOnly.ParseExact(yearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return parsed.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }
}
