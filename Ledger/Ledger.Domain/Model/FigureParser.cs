using System.Globalization;
using System.Text.RegularExpressions;

namespace Index.Domain.Model;

/// <summary>Which kind of figure a box holds. It decides what the parser accepts and what the hint under a
/// rejected box says: a money figure may carry a leading <c>$</c> and thousands commas, a percent may
/// carry a trailing <c>%</c>, and neither may carry the other's furniture.</summary>
public enum FigureKind
{
    Money,
    Percent,
}

public enum FigureStatus
{
    /// <summary>Nothing typed (or only spaces). Not an error and not a value.</summary>
    Empty,
    Valid,
    Invalid,
}

public readonly record struct FigureResult(FigureStatus Status, decimal Value);

/// <summary>
/// The one place a typed figure becomes a <see cref="decimal"/> — Accounts' post-it and the Add form
/// both read their balance, APR and payment through it (R-G16).
///
/// <para>It exists because the two pages each had their own <c>decimal.TryParse</c> and both were wrong
/// the same way: they read "unparseable" as "null". On the post-it, typing "abc" into APR cleared a stored
/// APR; on the Add form, the mockup's own example "6.25%" (a <c>%</c> <c>decimal.TryParse</c> rejects) saved
/// the account with no APR, which silently left it out of the Snowball. A figure that cannot be read is
/// <see cref="FigureStatus.Invalid"/> — a third state a caller must handle, which is the whole point —
/// never a null.</para>
///
/// <para>Accepts: surrounding spaces, an optional leading <c>-</c> (money only, and only where the caller
/// allows it — an overdrawn balance), a leading <c>$</c> (money), well-formed thousands commas (money),
/// a decimal point, and a trailing <c>%</c> (percent). Rejects everything else — "1,2,3", "6.25.1", "1e5",
/// "abc", a <c>%</c> on money, a <c>$</c> on a percent. Culture-invariant: the phone's locale never
/// changes what "1,200" means.</para>
/// </summary>
public static class FigureParser
{
    // ASCII digits only: \d would also match other scripts' digits, which decimal.Parse then rejects or,
    // worse, which look like numbers to the owner and are not.
    private static readonly Regex MoneyShape = new(
        @"^(?<neg>-)?\$?(?<num>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]*)(?:\.[0-9]*)?)$", RegexOptions.Compiled);

    private static readonly Regex PercentShape = new(
        @"^(?<num>[0-9]*(?:\.[0-9]*)?)\s*%?$", RegexOptions.Compiled);

    public static FigureResult Parse(string? text, FigureKind kind, bool allowNegative = false)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new FigureResult(FigureStatus.Empty, 0m);
        }

        string number;
        var negative = false;
        if (kind == FigureKind.Money)
        {
            var match = MoneyShape.Match(trimmed);
            if (!match.Success)
            {
                return Invalid;
            }

            negative = match.Groups["neg"].Success;
            if (negative && !allowNegative)
            {
                return Invalid;
            }

            number = match.Groups["num"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            var match = PercentShape.Match(trimmed);
            if (!match.Success)
            {
                return Invalid;
            }

            number = match.Groups["num"].Value;
        }

        // "$", "." and "%" alone shape-match but name no number.
        if (!number.Any(char.IsAsciiDigit))
        {
            return Invalid;
        }

        if (!decimal.TryParse(
                number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return Invalid; // too large for a decimal
        }

        return new FigureResult(FigureStatus.Valid, negative ? -value : value);
    }

    /// <summary>The line under a rose box, saying how to fix it (mockup 4/15: "a bad figure turns the box rose
    /// with a line saying how to fix it").</summary>
    public static string FixHint(FigureKind kind) => kind switch
    {
        FigureKind.Percent => "Type a number like 6.25.",
        _ => "Type an amount like 250 or 1,200.50.",
    };

    /// <summary>A stored figure as it is written back into its box.</summary>
    public static string Format(decimal value, FigureKind kind) =>
        value.ToString(kind == FigureKind.Percent ? "0.##" : "0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The number alone, for a box that draws its own "$" prefix or "%" suffix (mockup 15's boxed figures):
    /// "18,640.52", "6.75". Grouped like <see cref="FormatForBox"/> and, like it, unrounded, so it reads
    /// back through <see cref="Parse"/> as the same figure and a second format writes the same text.
    /// </summary>
    public static string FormatBare(decimal value, FigureKind kind) =>
        kind == FigureKind.Percent
            ? value.ToString("0.############################", CultureInfo.InvariantCulture)
            : (value < 0 ? "-" : string.Empty)
              + Math.Abs(value).ToString("#,##0.00############################", CultureInfo.InvariantCulture);

    /// <summary>
    /// What a box shows once a valid figure has been left (mockup 4): "$12,345.67", "$250.00", "6.25%". It
    /// reads back through <see cref="Parse"/> as the same figure — nothing is rounded, so a stray fraction of
    /// a cent or a third decimal place on a rate is shown rather than silently changed — and a second blur
    /// writes the same text. Invariant culture, like the parser.
    /// </summary>
    public static string FormatForBox(decimal value, FigureKind kind)
    {
        if (kind == FigureKind.Percent)
        {
            return value.ToString("0.############################", CultureInfo.InvariantCulture) + "%";
        }

        var amount = Math.Abs(value).ToString("#,##0.00############################", CultureInfo.InvariantCulture);
        return (value < 0 ? "-$" : "$") + amount;
    }

    private static FigureResult Invalid => new(FigureStatus.Invalid, 0m);
}

/// <summary>The furniture a boxed figure draws around its number: a "$" before money, a "%" after a rate.</summary>
public static class FigureAffix
{
    public static string? PrefixOf(FigureKind kind) => kind == FigureKind.Money ? "$" : null;

    public static string? SuffixOf(FigureKind kind) => kind == FigureKind.Percent ? "%" : null;
}
