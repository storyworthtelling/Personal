using System.Globalization;

namespace Index.Domain.Handoff;

/// <summary>
/// One month's Groceries figures as they cross to Constitution: a <c>yyyy-MM</c> month and two amounts that
/// travel as exact invariant-culture text with two decimals, never a double and never locale text. Built only
/// through <see cref="TryCreate"/>, which refuses a bad month, a negative amount, more than a cent of
/// precision, or a figure past <see cref="Ceiling"/>.
/// </summary>
public sealed record ConstitutionBudgetPayload
{
    /// <summary>The largest amount sent; anything above is refused rather than rounded or truncated.</summary>
    public const decimal Ceiling = 9_999_999.99m;

    private ConstitutionBudgetPayload(string month, decimal budget, decimal spent)
    {
        Month = month;
        Budget = budget;
        Spent = spent;
    }

    public string Month { get; }
    public decimal Budget { get; }
    public decimal Spent { get; }

    public string BudgetText => Format(Budget);
    public string SpentText => Format(Spent);

    public static ConstitutionBudgetPayload? TryCreate(string month, decimal budget, decimal spent)
    {
        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || !IsSendable(budget) || !IsSendable(spent))
        {
            return null;
        }

        return new ConstitutionBudgetPayload(month, budget, spent);
    }

    /// <summary>For live figures: a month whose refunds exceed its spending reads as nothing spent, because
    /// the contract carries no negative amounts.</summary>
    public static ConstitutionBudgetPayload? FromFigures(string month, decimal budget, decimal spent) =>
        TryCreate(month, budget, Math.Max(0m, spent));

    public string ToStoredText() => $"{Month}|{BudgetText}|{SpentText}";

    public static ConstitutionBudgetPayload? TryParseStored(string? stored)
    {
        var parts = stored?.Split('|');
        if (parts is not { Length: 3 }
            || !decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var budget)
            || !decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var spent))
        {
            return null;
        }

        return TryCreate(parts[0], budget, spent);
    }

    private static bool IsSendable(decimal amount) =>
        amount >= 0m && amount <= Ceiling && decimal.Round(amount, 2) == amount;

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
