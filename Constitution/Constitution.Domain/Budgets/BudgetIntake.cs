namespace Constitution.Domain.Budgets;

public enum BudgetIntakeOutcome
{
    Kept,
    Dropped,
}

/// <summary>
/// What the receiver does with a broadcast, without Android in it: read the extras, store the payload, leave one trail line.
/// A trail line names the act and, for a refusal, the field; it never carries an amount or a month.
///
/// <para>A store that fails throws out of here with its own message and stack. The receiver at the platform edge is what logs
/// it; swallowing it here would turn a lost budget into a "kept" line.</para>
/// </summary>
public sealed class BudgetIntake
{
    private readonly IMonthlyBudgets store;
    private readonly Action<string> trail;

    public BudgetIntake(IMonthlyBudgets store, Action<string> trail)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(trail);
        this.store = store;
        this.trail = trail;
    }

    public BudgetIntakeOutcome Receive(Func<string, string?> extra, DateTimeOffset now)
    {
        var parsed = BudgetPayload.Parse(extra);
        if (parsed.Payload is null)
        {
            trail($"budget broadcast dropped: the {parsed.RefusedField} extra did not parse");
            return BudgetIntakeOutcome.Dropped;
        }

        store.Receive(parsed.Payload, now);
        trail("budget broadcast kept");
        return BudgetIntakeOutcome.Kept;
    }
}
