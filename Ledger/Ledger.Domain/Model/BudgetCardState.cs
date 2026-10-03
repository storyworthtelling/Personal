namespace Index.Domain.Model;

public enum BudgetCardKind
{
    /// <summary>D94-D97/D142: spent so far, no limit, no meter, no "left".</summary>
    TrackingOnly,

    /// <summary>D49: the worksheet reader could not make the limit out; waiting, not wrong.</summary>
    Unreadable,

    /// <summary>No limit has been agreed for this category: say so, rather than "$0.00 left".</summary>
    NoLimit,

    WithinLimit,

    /// <summary>D48: the one alarm.</summary>
    OverLimit,
}

/// <summary>Which of a budget card's five states a category is in, with the figures each reads — pulled out
/// of the page so the rule is testable (the page only draws it).</summary>
public readonly record struct BudgetCardState(BudgetCardKind Kind, decimal Spent, decimal Limit, decimal Remaining, decimal Fraction)
{
    public static BudgetCardState Decide(decimal limit, decimal spent, bool trackingOnly, bool limitUnreadable)
    {
        if (trackingOnly)
        {
            return new BudgetCardState(BudgetCardKind.TrackingOnly, spent, limit, 0m, 0m);
        }
        if (limitUnreadable)
        {
            return new BudgetCardState(BudgetCardKind.Unreadable, spent, limit, 0m, 0m);
        }
        if (limit <= 0m)
        {
            return new BudgetCardState(BudgetCardKind.NoLimit, spent, 0m, 0m, 0m);
        }

        var remaining = limit - spent;
        var kind = spent > limit ? BudgetCardKind.OverLimit : BudgetCardKind.WithinLimit;
        return new BudgetCardState(kind, spent, limit, remaining, spent / limit);
    }
}
