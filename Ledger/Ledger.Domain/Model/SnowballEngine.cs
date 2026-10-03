namespace Index.Domain.Model;

/// <summary>
/// One loan's inputs to the Debt Snowball sheet's own formulas (D134;
/// INDEX-P11-TECHNICAL-FINDING.md Appendix A, columns B-D), given in FOCUS order — the order loans are
/// paid down in, not necessarily balance or alphabetical order. Money is <c>decimal</c> throughout
/// (MoneyIsDecimalTests, finding Correction 2); <see cref="Apr"/> is the yearly fraction (e.g.
/// <c>0.2650</c>), matching the sheet's own column C.
/// </summary>
public sealed record SnowballLoanInput(string Name, decimal Balance, decimal Apr, decimal WeeklyPayment);

/// <summary>
/// One loan's computed row (Appendix A's columns E-I), alongside its own inputs (B-D) for display.
/// </summary>
public sealed record SnowballRow(
    string Name,
    decimal Balance,
    decimal Apr,
    decimal WeeklyPayment,
    decimal FocusedWeeklyPayment,
    int WeeksBeforeFocused,
    decimal AmountBeforeFocused,
    int WeeksToPayoff,
    DateOnly PayoffDate);

/// <summary>
/// Guard G7 (D134): the sheet's own predictive formulas ("Loans Today", Appendix A), reproduced exactly
/// rather than replaced with a balance-based simulation — the appendix is explicit that the two disagree
/// (a balance-based model moves the debt-free date 19 weeks later) and that the sheet's own formulas are
/// the owner's chosen answer, not a bug to "correct". Pure and stateless: everything the formulas need
/// (the loans, in focus order, the one overall extra weekly payment, and the last payment date) is
/// passed in, and the whole schedule is recomputed fresh from those inputs every time — which is what
/// lets "every date recalculates as you type" (the mockup's own words for the Extra field) be true.
/// </summary>
public static class SnowballEngine
{
    /// <summary>
    /// Computes every row's E-I columns for <paramref name="loansInFocusOrder"/>, given the plan's one
    /// overall <paramref name="extraWeeklyPayment"/> (added to the very first loan's own weekly payment,
    /// D05/D73) and the sheet's own anchor date, <paramref name="lastPaymentDate"/>.
    /// </summary>
    public static IReadOnlyList<SnowballRow> Calculate(
        IReadOnlyList<SnowballLoanInput> loansInFocusOrder,
        decimal extraWeeklyPayment,
        DateOnly lastPaymentDate)
    {
        var rows = new List<SnowballRow>(loansInFocusOrder.Count);

        var focusedWeeklyPayment = 0m; // E, cumulative
        var weeksBeforeFocused = 0;    // F, for the row about to be built
        var payoffDate = lastPaymentDate; // running I

        for (var n = 0; n < loansInFocusOrder.Count; n++)
        {
            var loan = loansInFocusOrder[n];

            focusedWeeklyPayment = n == 0
                ? loan.WeeklyPayment + extraWeeklyPayment
                : focusedWeeklyPayment + loan.WeeklyPayment;

            var f = weeksBeforeFocused;

            // Appendix A's "row 2 quirk": the live sheet's own row 2 multiplies by E instead of D, but
            // this is harmless there because F is 0 on that row. The general rule below always uses D
            // (the loan's own weekly payment) — with F = 0 the two formulas agree, so no row is
            // special-cased, exactly as the appendix instructs.
            var amountBeforeFocused = Math.Round(
                loan.Balance - loan.WeeklyPayment * f * (1 - loan.Apr), 2, MidpointRounding.AwayFromZero);

            var weeksToPayoff = amountBeforeFocused == 0m
                ? 0
                : WeeksToPayoffFrom(amountBeforeFocused, loan.Apr, focusedWeeklyPayment);

            payoffDate = payoffDate.AddDays(7 * weeksToPayoff);

            rows.Add(new SnowballRow(
                loan.Name, loan.Balance, loan.Apr, loan.WeeklyPayment,
                focusedWeeklyPayment, f, amountBeforeFocused, weeksToPayoff, payoffDate));

            weeksBeforeFocused = f + weeksToPayoff; // F for the NEXT row: H[n] + F[n]
        }

        return rows;
    }

    /// <summary>
    /// Excel's <c>NPER</c>, worked out directly from the annuity formula (.NET has no built-in NPER):
    /// the number of weekly payments of <paramref name="payment"/> needed to amortise
    /// <paramref name="presentValue"/> at a weekly rate of <paramref name="apr"/>/52, with no future
    /// value and payments at period end — then <c>CEILING.MATH</c>'d up to a whole week. A zero APR
    /// (never seen in the appendix's own ten loans, but a real edge case for a 0% promo loan) has no
    /// logarithm to take: the balance drops by exactly <paramref name="payment"/> every week, so it is
    /// worked out as plain division rather than through the rate-based formula, which would divide by
    /// <c>ln(1)=0</c>.
    /// </summary>
    private static int WeeksToPayoffFrom(decimal presentValue, decimal apr, decimal payment)
    {
        var rate = (double)(apr / 52m);
        var pv = (double)presentValue;
        var pmt = (double)payment;

        var weeks = rate == 0
            ? pv / pmt
            : -Math.Log(1 - rate * pv / pmt) / Math.Log(1 + rate);

        // CEILING.MATH: round UP to the next whole week, never down, never to nearest. Subtracting a
        // tiny epsilon first keeps floating-point dust just below an exact integer (e.g.
        // 17.000000002) from ceiling-ing to the wrong next week.
        return (int)Math.Ceiling(weeks - 1e-9);
    }
}
