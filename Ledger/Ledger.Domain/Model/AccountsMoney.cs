using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// How Accounts prints an amount (Phase 4, "Money and type"; mockups 2, 3, 14, 15). A <b>balance</b> keeps
/// its cents ("$3,846.20"); a <b>limit or a payment</b> is whole dollars ("of $23,000", "Due Oct 6 · $285"),
/// because those are figures the owner reads at a glance, not ones they reconcile. A negative is written
/// with the real minus sign (U+2212) — a hyphen is a different glyph, narrower, and sits at the wrong
/// height against a digit — as mockup 3 draws a payment's "−$520.00".
///
/// <para>Whole dollars round to the nearest dollar (half away from zero). They are a way of SAYING a
/// figure, never of storing one: nothing here writes back.</para>
/// </summary>
public static class AccountsMoney
{
    /// <summary>The real minus sign, U+2212.</summary>
    public const string Minus = "−";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"$3,846.20", "−$520.00"; another currency is named first ("CAD $12.00").</summary>
    public static string Cents(decimal value, string? isoCurrencyCode)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return Compose(rounded < 0, Math.Abs(rounded).ToString("C2", Us), isoCurrencyCode);
    }

    /// <summary>"$285", "$23,000", "−$520"; the same currency rule as <see cref="Cents"/>.</summary>
    public static string Whole(decimal value, string? isoCurrencyCode)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return Compose(rounded < 0, Math.Abs(rounded).ToString("C0", Us), isoCurrencyCode);
    }

    private static string Compose(bool negative, string amount, string? isoCurrencyCode)
    {
        var text = negative ? Minus + amount : amount;
        return string.IsNullOrEmpty(isoCurrencyCode) || isoCurrencyCode == "USD" ? text : $"{isoCurrencyCode} {text}";
    }
}
