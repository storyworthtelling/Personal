namespace Index.Domain.Worksheet;

/// <summary>
/// Release H: one savings goal's set-aside LIMIT for one month — the monthly contribution the owner
/// wrote (or accepted) on the worksheet/follow-up, distinct from <c>SavingsGoal.TargetAmount</c> (the
/// account's overall goal, Release G) and from the extra debt payment (which has an existing home:
/// <c>SnowballPlan.ExtraPaymentAmount</c>, read/written through <c>SnowballService</c>'s own public API
/// — D98's own point that it should be ONE reserved figure, not two uncross-checked numbers). There was
/// no existing place to hold "this month's planned savings contribution," so this is new.
/// </summary>
public sealed class WorksheetSetAsideAmount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BudgetMonthId { get; set; }
    public Guid SavingsAccountId { get; set; }

    public decimal LimitAmount { get; set; }

    // D49's same rule as a category's own LimitUnreadable: the reader couldn't make this box out, so
    // it is flagged rather than guessed.
    public bool LimitUnreadable { get; set; }
}
