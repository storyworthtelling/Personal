using System.Globalization;
using Constitution.Domain.Budgets;

namespace Constitution.Domain.Groceries;

public enum GroceryBudgetState
{
    /// <summary>Index has not sent a usable budget for the month. Never read as zero, and never a flag.</summary>
    Unknown,

    /// <summary>The month's spend (and the week's estimate, when one was weighed) is inside the budget.</summary>
    Within,

    /// <summary>The month's spend (with the week's estimate, when one was weighed) is past the budget. The sheet's flag.</summary>
    Over,
}

/// <summary>
/// The grocery budget as the sheet shows it: a state, the exact amounts that apply to it (the rest are null, so an unknown budget
/// is never a zero), and the sentence in plain words. <b>It is page content, not a log line:</b> <see cref="ToString"/> names the
/// state and nothing else, so an interpolation into a log or trail cannot carry money.
/// </summary>
public sealed record GroceryBudgetStatus
{
    public GroceryBudgetState State { get; init; }

    public string Month { get; init; } = string.Empty;

    public decimal? Budget { get; init; }

    public decimal? Spent { get; init; }

    /// <summary>What is left (budget less spend and any estimate); set only when <see cref="GroceryBudgetState.Within"/>.</summary>
    public decimal? Remaining { get; init; }

    /// <summary>How far past the budget; set only when <see cref="GroceryBudgetState.Over"/>.</summary>
    public decimal? OverBy { get; init; }

    public string Sentence { get; init; } = string.Empty;

    public override string ToString() => nameof(GroceryBudgetStatus) + "(" + State + ")";
}

/// <summary>
/// C-6.4.3 · Index's grocery budget and spend (<see cref="MonthlyBudgets"/>) read for the meal sheet. Pure, exact (<see cref="decimal"/>
/// throughout, text in the invariant culture), and silent when nothing is known: no broadcast yet, or a budget of nothing (taken as
/// "Index has none set"), gives <see cref="GroceryBudgetState.Unknown"/> and no flag.
/// </summary>
public static class GroceryBudget
{
    /// <summary>The month a week's groceries are counted in: the month its Monday falls in, as <c>yyyy-MM</c>.</summary>
    public static string MonthOf(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <exception cref="ArgumentException">The month is not <c>yyyy-MM</c>.</exception>
    public static GroceryBudgetStatus Read(MonthlyBudgets budgets, string month) => Weigh(budgets, month, null);

    /// <summary>
    /// The month against its spend plus <paramref name="weekEstimate"/>, the week's groceries still to be bought (null: just the month so
    /// far). The estimate is held against what has actually been spent, not the budget alone.
    /// </summary>
    /// <exception cref="ArgumentException">The month is not <c>yyyy-MM</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The estimate is negative.</exception>
    public static GroceryBudgetStatus Weigh(MonthlyBudgets budgets, string month, decimal? weekEstimate)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        if (!BudgetPayload.IsMonth(month))
        {
            throw new ArgumentException("The month is not of the form yyyy-MM.", nameof(month));
        }

        if (weekEstimate is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(weekEstimate), "An estimate is never negative.");
        }

        var held = budgets.Find(month);
        if (held is null || held.Budget <= 0m)
        {
            return new GroceryBudgetStatus
            {
                State = GroceryBudgetState.Unknown,
                Month = month,
                Sentence = "Index has not sent a grocery budget for this month yet.",
            };
        }

        var budget = BudgetPayload.FormatAmount(held.Budget);
        var projected = held.Spent + (weekEstimate ?? 0m);
        if (projected > held.Budget)
        {
            var over = projected - held.Budget;
            var lead = weekEstimate is null ? string.Empty : "This week's groceries would run ";
            return new GroceryBudgetStatus
            {
                State = GroceryBudgetState.Over,
                Month = month,
                Budget = held.Budget,
                Spent = held.Spent,
                OverBy = over,
                Sentence = lead + BudgetPayload.FormatAmount(over) + " over this month's " + budget + " grocery budget.",
            };
        }

        var left = held.Budget - projected;
        return new GroceryBudgetStatus
        {
            State = GroceryBudgetState.Within,
            Month = month,
            Budget = held.Budget,
            Spent = held.Spent,
            Remaining = left,
            Sentence = weekEstimate is null
                ? BudgetPayload.FormatAmount(left) + " left of this month's " + budget + " grocery budget."
                : "This week's groceries fit: " + BudgetPayload.FormatAmount(left) + " would be left of this month's " + budget + " grocery budget.",
        };
    }
}
