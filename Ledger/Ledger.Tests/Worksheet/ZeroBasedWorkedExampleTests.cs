using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Storyworthtelling.Handwriting;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Mockup 13 reproduced from its own numbers through the real services (see <see cref="WorkedExample"/>).
/// Each test names the P11 remediation finding it closes (INDEX-P11-REMEDIATION-PLAN.md §2.1) and the
/// guard it is, where it is one.
/// </summary>
public class ZeroBasedWorkedExampleTests
{
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private static readonly (string, decimal)[] MockupB =
    [
        ("Groceries", 690m), ("Dining out", 284m), ("Fuel", 162m), ("Household", 174m), ("Kids", 228m),
        ("Utilities", 286m), ("Subscriptions", 68m), ("Medical", 85m), ("Clothing", 248m), ("Gifts", 158m),
    ];

    private static readonly (string, decimal)[] MockupC =
    [
        ("Groceries", 565m), ("Dining out", 192m), ("Fuel", 162m), ("Household", 174m), ("Kids", 222m),
        ("Utilities", 286m), ("Subscriptions", 68m), ("Medical", 85m), ("Clothing", 64m), ("Gifts", 43m),
    ];

    // What the owner wrote in the Extra debt payment box on each page: the (a) page's $690 and the (b)
    // page's $490, both inside the $3,100 (last month's actual was $350).
    private const decimal ExtraOver = 690m;
    private const decimal ExtraUnder = 490m;

    // ---- finding 8: is the extra payment counted twice? ----

    [Fact]
    public async Task MockupThirteenA_TheTargetIsIncomeLessTheLoansPaymentsOnly_TheExtraPaymentIsInsideIt()
    {
        // Mockup 13(a): $9,350 income, "Snowball payments, 5 Fridays  -$6,250", "Left to assign $1,194",
        // and the intro's "$3,100". 6,250 = 5 x 1,250, Appendix A's D13 (the loans' own weekly payments,
        // with no extra in it); the $350 extra is a BOX inside the $3,100, not a deduction from it.
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());

        var plan = await example.PlanBuilder.BuildAsync("2026-10", default);

        Assert.Equal(5, plan.FridayCount);
        Assert.Equal(6_250, plan.SnowballPaymentsWholeDollars);
        Assert.Equal(1_194, plan.LeftToAssignWholeDollars);
        Assert.Equal(3_100, plan.ZeroBasedTargetWholeDollars);
        Assert.Equal(350m, plan.SetAsideLinesOrEmpty.Single(l => l.Kind == SetAsideLineKind.ExtraDebtPayment).LastMonthActual);
    }

    [Fact]
    public async Task MockupThirteenC_WhatWasWrittenCountsTheExtraPaymentOnce_AndMatchesThePrintedFigures()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        await example.WriteCategoryLimitsAsync("2026-10", MockupC);
        await example.Snowball.SetExtraPaymentAmountAsync(ExtraUnder, default);

        var month = await example.MonthAsync("2026-10");
        Assert.Equal(3_100, month.PrintedZeroBasedTargetWholeDollars);
        var plan = await example.FollowUpBuilder.BuildAsync(month, default);

        // "What we wrote comes to $2,711 ... so $389 has none yet", with the $490 inside the 3,100.
        Assert.NotNull(plan);
        Assert.False(plan!.IsOver);
        Assert.Equal(2_711, plan.WrittenTotalWholeDollars);
        Assert.Equal(389, plan.DifferenceWholeDollars);
        Assert.Equal(3_100, plan.TargetWholeDollars);
        Assert.Equal(879, plan.ActionableLines.Single(l => l.Key == WorksheetRegions.SetAsideExtraPayment).SuggestedWholeDollars);
        Assert.True(plan.WithSuggestionsBalances);
    }

    // ---- finding 3, guard R-G14: a blank savings box keeps last month's figure ----

    [Fact]
    public async Task R_G14_ABlankSavingsBox_KeepsThePrintedFigure_SoTheBalanceCheckSumsTwoTwentyFiveAndOneThirtyFive()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        var month = await example.MonthAsync("2026-10");

        // The owner agreed to both savings lines and wrote nothing on either box.
        var savings = (await example.Budget.GetSavingsSetAsideAmountsAsync(month.Id, default))
            .ToDictionary(a => a.SavingsAccountId, a => a.LimitAmount);
        Assert.Equal(225m, savings[example.EmergencyId]);
        Assert.Equal(135m, savings[example.SummerId]);

        var written = await example.Budget.GetWrittenLimitsForBalanceCheckAsync(month.Id, default);
        Assert.Contains(225m, written);
        Assert.Contains(135m, written);

        // Mockup 13(c): the "Wrote" column shows $225 and $135, not $0.
        await example.WriteCategoryLimitsAsync("2026-10", MockupC);
        await example.Snowball.SetExtraPaymentAmountAsync(ExtraUnder, default);
        var plan = await example.FollowUpBuilder.BuildAsync(await example.MonthAsync("2026-10"), default);
        var emergency = plan!.ActionableLines.Single(l => l.Key == WorksheetRegions.SetAsideSavings(example.EmergencyId));
        var summer = plan.ActionableLines.Single(l => l.Key == WorksheetRegions.SetAsideSavings(example.SummerId));
        Assert.Equal(225, emergency.WrittenWholeDollars);
        Assert.Equal(135, summer.WrittenWholeDollars);
    }

    // ---- finding 2, guard R-G13: a delivered worksheet is read back the next morning ----

    [Fact]
    public async Task R_G13_ADeliveredSheet_IsReadBackTheMorningAfter_AppliesItsLimits_AndStartsTheFollowUpCycle()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        Assert.Equal(BudgetMonthStatus.DeliveredToRemarkable, (await example.MonthAsync("2026-10")).Status);
        example.TabletNowHoldsLastUpload();
        example.Extractor.Notes =
        [
            new ParsedNote { Text = "690", Column = WorksheetRegions.CategoryLimit(example.CategoryIds["Groceries"]), Confidence = 0.95 },
        ];

        await example.RunCycleOnAsync(Oct1); // "printed Sep 30 ... read the morning after"

        var october = await example.MonthAsync("2026-10");
        var groceries = (await example.Budget.GetCategoryAmountsAsync(october.Id, default)).Single(a => a.CategoryId == example.CategoryIds["Groceries"]);
        Assert.Equal(690m, groceries.LimitAmount); // "the limits are live from the first read"
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, october.Status); // only the zero check is pending
        Assert.Single(example.Remarkable.Uploads); // silent, and the follow-up has not been sent on the same morning

        await example.RunCycleOnAsync(Oct2); // "follow-up Oct 2, arrives the next morning"

        var followUp = Assert.Single(example.Remarkable.Uploads, u => u.Title == WorksheetTitles.FollowUpCurrent);
        Assert.NotEmpty(followUp.PdfBytes);
    }

    [Fact]
    public async Task R_G13_TheFirstReadBackHappensOncePerDeliveredSheet()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        example.TabletNowHoldsLastUpload();
        example.Extractor.Notes =
        [
            new ParsedNote { Text = "690", Column = WorksheetRegions.CategoryLimit(example.CategoryIds["Groceries"]), Confidence = 0.95 },
        ];
        await example.RunCycleOnAsync(Oct1);

        // The owner (or a later follow-up) changes the figure; a second wake the same morning must not
        // put the sheet's original ink back over it.
        await example.WriteCategoryLimitsAsync("2026-10", ("Groceries", 123m));
        await example.RunCycleOnAsync(Oct1);

        var october = await example.MonthAsync("2026-10");
        var groceries = (await example.Budget.GetCategoryAmountsAsync(october.Id, default)).Single(a => a.CategoryId == example.CategoryIds["Groceries"]);
        Assert.Equal(123m, groceries.LimitAmount);
    }

    [Fact]
    public async Task R_G13_TheMonthEndReadBack_DoesNotPutTheSheetsInkBackOverALaterCorrection()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        example.TabletNowHoldsLastUpload();
        example.Extractor.Notes =
        [
            new ParsedNote { Text = "690", Column = WorksheetRegions.CategoryLimit(example.CategoryIds["Groceries"]), Confidence = 0.95 },
        ];
        await example.RunCycleOnAsync(Oct1);
        await example.WriteCategoryLimitsAsync("2026-10", ("Groceries", 548m)); // a follow-up's correction

        await example.RunCycleOnAsync(Oct31); // the old last-day read of October's sheet

        var october = await example.MonthAsync("2026-10");
        var groceries = (await example.Budget.GetCategoryAmountsAsync(october.Id, default)).Single(a => a.CategoryId == example.CategoryIds["Groceries"]);
        Assert.Equal(548m, groceries.LimitAmount);
    }

    [Fact]
    public async Task R_G13_ASheetThatCouldNotBeReadTheFirstMorning_IsTriedAgainTheNext()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        // Nothing on the tablet yet (unreachable, or not synced): the morning's read finds no document.
        example.Remarkable.Existing = null;
        await example.RunCycleOnAsync(Oct1);
        Assert.Equal(BudgetMonthStatus.DeliveredToRemarkable, (await example.MonthAsync("2026-10")).Status);

        example.TabletNowHoldsLastUpload();
        await example.RunCycleOnAsync(Oct2);

        Assert.NotEqual(BudgetMonthStatus.DeliveredToRemarkable, (await example.MonthAsync("2026-10")).Status);
    }

    // ---- finding 6: the over case lists the rows in trim order ----

    [Fact]
    public async Task MockupThirteenB_TheOverCaseListsClothingGroceriesGifts_LargestRiseFirst()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        await example.WriteCategoryLimitsAsync("2026-10", MockupB);
        await example.Snowball.SetExtraPaymentAmountAsync(ExtraOver, default);

        var plan = await example.FollowUpBuilder.BuildAsync(await example.MonthAsync("2026-10"), default);

        // Wrote 2,383 across the ten categories + 690 + 225 + 135 = 3,433, which is 333 over the 3,100.
        // Largest rise over September first: Clothing (+144) back to 104, Groceries (+142) back to 548,
        // then Gifts takes only what is left of the 333: 158 - (333 - 144 - 142) = 111.
        Assert.True(plan!.IsOver);
        Assert.Equal(333, plan.DifferenceWholeDollars);
        Assert.Equal(3_433, plan.WrittenTotalWholeDollars);
        Assert.Equal(["Clothing", "Groceries", "Gifts"], plan.ActionableLines.Select(l => l.Name));
        Assert.Equal([104L, 548L, 111L], plan.ActionableLines.Select(l => l.SuggestedWholeDollars!.Value));
    }

    // ---- finding 5: the savings lines are named for their account or goal ----

    [Fact]
    public async Task MockupThirteenB_TheReferenceListNamesTheSavingsAccounts_NotSavingsTwice()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());
        await example.RunCycleOnAsync(Sep30);
        await example.WriteCategoryLimitsAsync("2026-10", MockupB);
        await example.Snowball.SetExtraPaymentAmountAsync(ExtraOver, default);
        var plan = await example.FollowUpBuilder.BuildAsync(await example.MonthAsync("2026-10"), default);

        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan!)));
        var text = document.GetPage(1).Text;

        Assert.Contains("Emergency fund", text, StringComparison.Ordinal);
        Assert.Contains("Summer trip", text, StringComparison.Ordinal);
    }

    // ---- finding 7: the target is recorded only if the page printed it ----

    [Fact]
    public async Task ASheetWithNoSetAsideSection_PrintsNoTarget_AndRecordsNone_SoNoBalanceCheckRuns()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset(), withLoans: false, withSavingsGoals: false);

        await example.RunCycleOnAsync(Sep30);

        var october = await example.MonthAsync("2026-10");
        Assert.Null(october.PrintedZeroBasedTargetWholeDollars);

        using var document = PdfDocument.Open(new MemoryStream(example.Remarkable.Uploads.Single().PdfBytes));
        Assert.DoesNotContain("Every dollar gets a job", document.GetPage(1).Text, StringComparison.Ordinal);

        // And the morning after: read, applied, and no follow-up page is ever delivered for it.
        example.TabletNowHoldsLastUpload();
        await example.RunCycleOnAsync(Oct1);
        await example.RunCycleOnAsync(Oct2);
        Assert.Single(example.Remarkable.Uploads);
        Assert.Null(await example.Budget.FindMonthNeedingFollowUpAsync(default));
    }

    [Fact]
    public async Task ASheetThatPrintsItsTarget_RecordsExactlyThatTarget()
    {
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset());

        await example.RunCycleOnAsync(Sep30);

        Assert.Equal(3_100, (await example.MonthAsync("2026-10")).PrintedZeroBasedTargetWholeDollars);
        using var document = PdfDocument.Open(new MemoryStream(example.Remarkable.Uploads.Single().PdfBytes));
        Assert.Contains("$3,100", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderWithNoExtraPaymentLine_TheSavingsLinesAreTheActionableRows_NotAnEmptyTable()
    {
        // No Snowball loan, two savings goals: the page prints a target, so a follow-up can be needed,
        // and an empty actionable table would be delivered every morning with nothing to act on.
        using var example = await WorkedExample.CreateAsync(Sep30.ToDateTimeOffset(), withLoans: false);
        await example.RunCycleOnAsync(Sep30);
        var month = await example.MonthAsync("2026-10");
        Assert.Equal(9_350, month.PrintedZeroBasedTargetWholeDollars);

        var plan = await example.FollowUpBuilder.BuildAsync(month, default);

        Assert.False(plan!.IsOver);
        Assert.Equal(
            [WorksheetRegions.SetAsideSavings(example.EmergencyId), WorksheetRegions.SetAsideSavings(example.SummerId)],
            plan.ActionableLines.Select(l => l.Key));
        Assert.All(plan.ActionableLines, l => Assert.Null(l.SuggestedWholeDollars));
        Assert.False(plan.RemainderGoesToExtraPayment);

        // And the page does not promise a suggestion it does not make.
        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
        var text = document.GetPage(1).Text;
        Assert.DoesNotContain("suggested for the Snowball", text, StringComparison.Ordinal);
        Assert.Contains("no suggestion for it", text, StringComparison.Ordinal);
    }
}

internal static class DateOnlyExtensions
{
    public static DateTimeOffset ToDateTimeOffset(this DateOnly day) =>
        new(day.Year, day.Month, day.Day, 9, 0, 0, TimeSpan.Zero);
}
