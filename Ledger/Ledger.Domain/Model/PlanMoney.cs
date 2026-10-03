using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// Money as Snowball, Budget and Savings print it (Phase 4, "Money and type"; mockups 6, 7, 8, 9, 16).
/// Two shapes, and the rule for which is the mockups': a <i>balance</i> keeps its cents
/// ("$7,412.36", "$186.40 left"), while a <i>limit, payment or weekly figure</i> is whole dollars
/// ("of $625", "$465 a week now", "$545/wk"). A negative figure wears U+2212, a true minus, which is what the
/// mockups draw and what a screen reader reads as "minus" — en-US currency formatting gives accountant's
/// brackets instead ("($12.50)"), which the mockups never use, and a hyphen-minus is not a minus sign.
///
/// <para>Always US dollars in the en-US shape. The Accounts page's own formatter also prefixes a non-USD
/// currency's ISO code; these three pages only ever hold USD (a loan, a budget and a savings account the
/// owner tracks), so the code is not carried here.</para>
/// </summary>
public static class PlanMoney
{
    private const string Minus = "−";
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"$7,412.36": cents kept. A balance, a total, an amount spent.</summary>
    public static string Balance(decimal value) => Format(Math.Round(value, 2, MidpointRounding.AwayFromZero), "N2");

    /// <summary>"$625": whole dollars, rounded away from zero at the half. A limit, a payment, a weekly figure.</summary>
    public static string Whole(decimal value) => Format(Math.Round(value, 0, MidpointRounding.AwayFromZero), "N0");

    /// <summary>
    /// A row that is taken away from a total, printed as its signed contribution: "−$6,250" for a payment
    /// of 6,250, and a plain "$500" for a figure of −500 (a refund that, once subtracted, adds). Putting a
    /// literal minus in front of <see cref="Whole"/> instead would print "−$−500", a minus twice over.
    /// </summary>
    public static string Deduction(decimal value) => value >= 0m ? Minus + Whole(value) : Whole(-value);

    private static string Format(decimal rounded, string format) =>
        (rounded < 0m ? Minus : string.Empty) + "$" + Math.Abs(rounded).ToString(format, Culture);
}
