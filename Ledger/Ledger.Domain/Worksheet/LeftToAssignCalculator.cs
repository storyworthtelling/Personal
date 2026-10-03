using System.Globalization;

namespace Index.Domain.Worksheet;

/// <summary>D155's printed "left to assign" line and the three whole-dollar figures above it.</summary>
public sealed record LeftToAssignResult(
    long IncomeWholeDollars,
    int FridayCount,
    long PaymentsWholeDollars,
    long LastMonthSpendingWholeDollars,
    long LeftToAssignWholeDollars);

/// <summary>
/// D155: "income, less the month's Snowball payments (the sheet's weekly total times the Fridays
/// that fall in the month), less last month's spending." Pure, and the whole reason it exists rather
/// than being inlined at the one call site: <see cref="FridaysInMonth(string)"/> is the one place that
/// answers "how many Fridays are really in this month," which Guard G20 pins against the wrong,
/// simpler-looking answer — a flat 52 &#247; 12 &#8776; 4.33 weeks a month, which would make every
/// month's payments line read identically regardless of whether it actually has four or five Fridays.
/// </summary>
public static class LeftToAssignCalculator
{
    public static int FridaysInMonth(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var count = 0;
        for (var day = 1; day <= daysInMonth; day++)
        {
            if (new DateOnly(year, month, day).DayOfWeek == DayOfWeek.Friday)
            {
                count++;
            }
        }
        return count;
    }

    public static int FridaysInMonth(string yearMonth)
    {
        var parsed = DateOnly.ParseExact(yearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return FridaysInMonth(parsed.Year, parsed.Month);
    }

    /// <summary>
    /// Each of the three printed lines is rounded from its own raw figure BEFORE the subtraction — income and
    /// last month's spending floored, the month's payments rounded UP (a payment never reads less than what is
    /// owed) — never combined first and rounded once at the end (Guard G19) — which also means the result below is
    /// automatically a whole dollar, with no further rounding step of its own to get wrong.
    /// </summary>
    public static LeftToAssignResult Calculate(
        decimal projectedMonthlyIncome, decimal weeklyPaymentTotal, int fridayCount, decimal lastMonthSpending)
    {
        var income = WholeDollars.Floor(projectedMonthlyIncome);
        var payments = WholeDollars.Ceiling(weeklyPaymentTotal * fridayCount);
        var lastMonth = WholeDollars.Floor(lastMonthSpending);
        var leftToAssign = income - payments - lastMonth;
        return new LeftToAssignResult(income, fridayCount, payments, lastMonth, leftToAssign);
    }
}
