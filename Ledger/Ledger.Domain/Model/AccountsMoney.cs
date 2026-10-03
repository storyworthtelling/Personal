using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// How Accounts prints an amount (Phase 4, "Money and type"; mockups 2, 3, 14, 15). A <b>balance</b> keeps
/// its cents ("$3,846.20"); a <b>limit</b> is whole dollars ("of $23,000") and a <b>payment</b> is whole dollars
/// rounded UP ("Due Oct 6 · $286" for 285.42), because those are figures the owner reads at a glance, not ones
/// they reconcile. A negative is written
/// with the real minus sign (U+2212) — a hyphen is a different glyph, narrower, and sits at the wrong
/// height against a digit — as mockup 3 draws a payment's "−$520.00".
///
/// <para>A limit rounds to the nearest dollar (half away from zero); a payment goes through
/// <see cref="Payment"/> and rounds up. Both are a way of SAYING a
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

    /// <summary>"$23,000", "−$520": a limit or other non-payment figure, nearest dollar; the same currency rule as
    /// <see cref="Cents"/>. A payment is <see cref="Payment"/>.</summary>
    public static string Whole(decimal value, string? isoCurrencyCode)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return Compose(rounded < 0, Math.Abs(rounded).ToString("C0", Us), isoCurrencyCode);
    }

    /// <summary>
    /// "$286" for 285.42: a payment in whole dollars, rounded UP. The rounding and the glyphs are
    /// <see cref="PlanMoney.Payment"/>'s (this is the single payment formatter; nothing here repeats it) and
    /// only the currency rule of <see cref="Cents"/> is added.
    /// </summary>
    public static string Payment(decimal value, string? isoCurrencyCode) =>
        NameCurrency(PlanMoney.Payment(value), isoCurrencyCode);

    private static string Compose(bool negative, string amount, string? isoCurrencyCode) =>
        NameCurrency(negative ? Minus + amount : amount, isoCurrencyCode);

    private static string NameCurrency(string text, string? isoCurrencyCode) =>
        string.IsNullOrEmpty(isoCurrencyCode) || isoCurrencyCode == "USD" ? text : $"{isoCurrencyCode} {text}";
}
