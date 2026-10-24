using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Budgets;

/// <summary>What reading a broadcast came to: the payload, or the name of the first field that was refused (never its value).</summary>
public sealed record BudgetParseResult(BudgetPayload? Payload, string? RefusedField)
{
    public bool IsValid => Payload is not null;
}

/// <summary>
/// One month's grocery budget and spend as Index sends them. Exact decimals, strict month; anything else is refused whole.
///
/// <para>An amount is plain ASCII digits with an optional point and fraction, read and written in the invariant culture. A
/// sign, a comma, an exponent, a space or a non-ASCII digit is refused, because the text is the figure: nothing here is ever
/// passed through a double, and nothing follows the phone's locale. The bounds (fifteen whole digits, six places) sit well
/// inside what a <see cref="decimal"/> holds, so reading is always exact.</para>
/// </summary>
public sealed record BudgetPayload
{
    // [0-9], not \d: .NET's \d matches every Unicode decimal digit, and "١٢" is not a figure Index sent.
    private static readonly Regex AmountShape = new("^[0-9]{1,15}(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex MonthShape = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public BudgetPayload(string month, decimal budget, decimal spent)
    {
        if (!IsMonth(month))
        {
            throw new ArgumentException("The month is not of the form yyyy-MM.", nameof(month));
        }

        if (budget < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), "A budget is never negative.");
        }

        if (spent < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(spent), "An amount spent is never negative.");
        }

        Month = month;
        Budget = budget;
        Spent = spent;
    }

    public string Month { get; }

    public decimal Budget { get; }

    public decimal Spent { get; }

    /// <summary>The budget as exact invariant text, with the scale it was read with (<c>412.50</c> stays <c>412.50</c>).</summary>
    public string BudgetText => FormatAmount(Budget);

    public string SpentText => FormatAmount(Spent);

    public static bool IsMonth(string? text) => text is not null && MonthShape.IsMatch(text);

    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0m;
        return text is not null
            && AmountShape.IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    public static string FormatAmount(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads the three extras, month first. The first one refused is named; the payload is all or nothing.</summary>
    public static BudgetParseResult Parse(Func<string, string?> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);

        var month = extra(BudgetContract.MonthExtra);
        if (!IsMonth(month))
        {
            return new BudgetParseResult(null, BudgetContract.MonthExtra);
        }

        if (!TryParseAmount(extra(BudgetContract.BudgetExtra), out var budget))
        {
            return new BudgetParseResult(null, BudgetContract.BudgetExtra);
        }

        if (!TryParseAmount(extra(BudgetContract.SpentExtra), out var spent))
        {
            return new BudgetParseResult(null, BudgetContract.SpentExtra);
        }

        return new BudgetParseResult(new BudgetPayload(month!, budget, spent), null);
    }
}
