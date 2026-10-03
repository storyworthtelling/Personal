namespace Index.Domain.Worksheet;

/// <summary>One category's printed row: its name, and last month's actual spend (mockup 12's own
/// "September" column) — already what the row prints, whole dollars aside.</summary>
public sealed record WorksheetCategoryLine(Guid CategoryId, string Name, decimal LastMonthActual);

/// <summary>
/// Everything the worksheet page needs, gathered once by <c>WorksheetPlanBuilder</c> (Infrastructure,
/// since it talks to <c>BudgetService</c>/<c>IIncomeDetectionService</c>/<c>SnowballService</c>) and
/// then handed unchanged to both <c>WorksheetPainter</c> (which draws it) and
/// <c>WorksheetReadBackInstruction</c> (which describes it back to the vision model) — one plan, one
/// source of the figures that appear on the page, per D151/D154/D155.
/// </summary>
public sealed record WorksheetPlan(
    string TargetYearMonth,
    DateOnly PrintedOn,
    long IncomeWholeDollars,
    bool IncomeConfident,
    int FridayCount,
    long SnowballPaymentsWholeDollars,
    long LastMonthSpendingWholeDollars,
    long? LeftToAssignWholeDollars,
    IReadOnlyList<WorksheetCategoryLine> Categories,
    IReadOnlyList<WorksheetSetAsideLine>? SetAsideLines = null)
{
    /// <summary>D103's exact wording for the income line when Index can't tell.</summary>
    public const string IncomeUnsureText = "Not sure — write it in";

    public IReadOnlyList<Guid> CategoryIdsInPrintedOrder => Categories.Select(c => c.CategoryId).ToList();

    /// <summary>Release H (D98&#8211;D100): the whole set-aside section, defaulting to empty for a plan
    /// built before Release H (or a test that never supplies one) so every existing caller keeps
    /// working unchanged.</summary>
    public IReadOnlyList<WorksheetSetAsideLine> SetAsideLinesOrEmpty => SetAsideLines ?? [];

    public IReadOnlyList<string> SetAsideKeysInPrintedOrder => SetAsideLinesOrEmpty.Select(l => l.Key).ToList();

    /// <summary>
    /// D100/D155's own target for the WHOLE page (categories + set-aside): income less the month's
    /// Snowball payments — NOT the ordinary worksheet's "left to assign" (which also subtracts last
    /// month's spending as a rough pre-writing guide). Mockup 13(a)'s own worked example: $9,350 income
    /// less $6,250 in Snowball payments is $3,100, the figure the intro states as the target — not the
    /// $1,194 "Left to assign" line directly below it, which is a different, older figure kept unchanged
    /// from mockup 12. Null when income isn't confident (D103): nothing is guessed onto the page, so
    /// there is no target to check written figures against.
    /// </summary>
    public long? ZeroBasedTargetWholeDollars => IncomeConfident ? IncomeWholeDollars - SnowballPaymentsWholeDollars : null;

    /// <summary>
    /// The target as the PAGE states it, which is the only target a balance check may ever be run
    /// against. The intro sentence ("the boxes should come to $3,100") is printed only for a sheet that
    /// has a Set aside section; one without it is Release F's plain page and states no target. Recording
    /// <see cref="ZeroBasedTargetWholeDollars"/> for such a sheet would check the owner's writing against
    /// a number nobody printed, and a month carrying that target is one the follow-up cycle then chases
    /// every morning. <c>WorksheetPainter</c> and the delivery service both read this one property, so
    /// what is drawn and what is checked cannot disagree.
    /// </summary>
    public long? PrintedTargetWholeDollars => SetAsideLinesOrEmpty.Count > 0 ? ZeroBasedTargetWholeDollars : null;
}
