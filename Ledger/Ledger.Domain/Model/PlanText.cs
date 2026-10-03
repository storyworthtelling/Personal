using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// The figures and sentences on Snowball's cards, Budget's cards and Savings' cards and post-its that
/// Phase 4 pinned to the mockups (6, 7, 8, 9, 16), as pure functions so a test can hold them to what the
/// mockup prints rather than a page being read for them. Money goes through <see cref="PlanMoney"/>: limits,
/// payments and weekly figures are whole dollars, balances keep their cents. Every apostrophe is U+2019.
/// </summary>
public static class PlanText
{
    /// <summary>Snowball's hint under "Extra each week" (mockup 6).</summary>
    public const string ExtraHint = "Added to the focus loan’s payment.";

    /// <summary>Savings' Growth footnote under a chart that has a goal (mockup 9).</summary>
    public const string GrowthFootnote =
        "Monthly, from each month’s last balance. The dotted line is the last year’s pace, carried forward.";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"Sep 26, 2031": the debt-free date, in the mockup's abbreviated form (it is set in mono at
    /// 28, where "September 26, 2031" would not fit the well on one line).</summary>
    public static string DebtFreeDate(DateOnly date) => date.ToString("MMM d, yyyy", Invariant);

    /// <summary>"Paid off Jul 16, 2027": a loan card's first line.</summary>
    public static string PaidOff(DateOnly date) => $"Paid off {date.ToString("MMM d, yyyy", Invariant)}";

    /// <summary>"$465 a week now": what a loan is paid each week today.</summary>
    public static string WeeklyNow(decimal weeklyPayment) => $"{PlanMoney.Whole(weeklyPayment)} a week now";

    /// <summary>"$545/wk": the focus loan's payment once it is the focus, on the post-it.</summary>
    public static string AsFocus(decimal weeklyPayment) => $"{PlanMoney.Whole(weeklyPayment)}/wk";

    /// <summary>"of $625": the limit under a Budget card's meter.</summary>
    public static string OfLimit(decimal limit) => $"of {PlanMoney.Whole(limit)}";

    /// <summary>" of $1,995": the month line's agreed total, set after the (cents-bearing) amount spent.</summary>
    public static string MonthOfAgreed(decimal agreed) => $" {OfLimit(agreed)}";

    /// <summary>"$305 a month": a savings account's pace.</summary>
    public static string Pace(decimal monthlyPace) => $"{PlanMoney.Whole(monthlyPace)} a month";

    /// <summary>"From the last 12 months’ growth." — and, because the possessive follows the count, "From the
    /// last 1 month’s growth." for a single month (it used to print "1 month’").</summary>
    public static string HistoryNote(int months) => months == 1
        ? "From the last 1 month’s growth."
        : $"From the last {months} months’ growth.";
}
