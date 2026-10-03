namespace Index.Domain.Model;

/// <summary>
/// D50/D82/D160: a savings goal is a target <b>amount only</b> — no target-date column, and none is
/// ever entered. The date a goal is likely reached is always worked out (<see cref="SavingsPace"/>,
/// D83), never typed, which is why this record carries nothing but the number. One row per savings
/// account (D50: "set per savings account, not as one household total"); <c>LedgerDbContext</c> puts a
/// unique index on <see cref="AccountId"/> to back that up.
///
/// <para>Edited on the account's own post-it, not a separate goals screen (D111, sharpened by D159/
/// D160): <c>SavingsService.SetGoalAsync</c> upserts this row, and Remove is undo-first
/// (<c>DogEarState.BeginUndo</c>) — the account stays, only this row goes.</para>
/// </summary>
public sealed class SavingsGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public decimal TargetAmount { get; set; }
}
