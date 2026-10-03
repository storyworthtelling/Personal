using System.Globalization;
using Index.Domain.Model;

namespace Index.Domain.Worksheet;

/// <summary>
/// D52/D89: the worksheet goes to the reMarkable "on its own" — no button, and its arrival is silent —
/// so something has to decide WHEN without a person in the loop. WorkManager has no native monthly
/// period (only a fixed <c>repeatInterval</c>, and calendar months are not a fixed number of days), so
/// <c>WorksheetDeliveryWorker</c> wakes on the same daily cadence <c>RefreshWorker</c> already uses and
/// asks this pure gate "is today the day?" instead. Pure so the gate is a decision, not a side effect.
/// </summary>
public static class WorksheetDeliverySchedule
{
    /// <summary>The last calendar day of the month — matching mockup 12's own footer ("printed Sep 30"
    /// on the page that opens October): next month's worksheet goes out as this month closes.</summary>
    public static bool IsDueToday(DateOnly today) => today.Day == DateTime.DaysInMonth(today.Year, today.Month);

    /// <summary>
    /// D153/D156, Guard R-G13: has the morning come on which a worksheet that nobody has read yet should
    /// be read? The worksheet for month M goes out on the last day of the month before it (the due day
    /// above), and mockup 13 draws the cycle that follows it: "printed Sep 30", read the next morning,
    /// follow-up "printed Oct 2". So a sheet is due its first read from the first day of the month it is
    /// FOR, and only while it has not been read — <see cref="BudgetMonthStatus.DeliveredToRemarkable"/> is
    /// the whole record of that, which makes the read once-per-sheet and idempotent: a wake that finds the
    /// tablet unreachable, or the extraction failing, leaves the status alone and the next wake tries again.
    ///
    /// <para>Reading it only on the last day of ITS month (the earlier behaviour) made an October sheet
    /// printed Sep 30 first readable on Oct 31, so its follow-up could not begin before Nov 1 and its
    /// written limits were not live for a month.</para>
    /// </summary>
    public static bool IsFirstReadBackDue(DateOnly today, string worksheetYearMonth, BudgetMonthStatus status)
    {
        if (status != BudgetMonthStatus.DeliveredToRemarkable)
        {
            return false;
        }
        var firstDay = DateOnly.ParseExact(worksheetYearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return today >= firstDay;
    }

    /// <summary>The month the worksheet is FOR, given today's date — always the month right after today.</summary>
    public static string TargetYearMonth(DateOnly today) =>
        today.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
