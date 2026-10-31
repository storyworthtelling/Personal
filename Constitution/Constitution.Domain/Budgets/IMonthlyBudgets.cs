namespace Constitution.Domain.Budgets;

/// <summary>Where the latest-per-month budgets are kept.</summary>
public interface IMonthlyBudgets
{
    MonthlyBudgets Load();

    /// <summary>Applies one payload to what is kept and keeps the result.</summary>
    void Receive(BudgetPayload payload, DateTimeOffset receivedAt);
}
