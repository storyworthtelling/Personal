using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// Pure calendar-month arithmetic for Budget (D66, mockup 7's month line): a <c>BudgetMonth.YearMonth</c>
/// is always "yyyy-MM", and every date-range/previous-month/days-left question a service or a screen
/// asks reduces to string-in, string-out functions here rather than being worked out ad hoc at each
/// call site (and, for the previous-month arithmetic, tested once rather than trusted to be right at
/// every call site that needs it — Guard G5 depends on <see cref="PreviousYearMonth"/> being exactly
/// right).
/// </summary>
public static class BudgetPeriod
{
    public static string YearMonthOf(DateOnly date) =>
        date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string YearMonthOf(DateTimeOffset date) => YearMonthOf(DateOnly.FromDateTime(date.Date));

    /// <summary>The first and last calendar day of the month named by "yyyy-MM".</summary>
    public static (DateOnly Start, DateOnly End) Range(string yearMonth)
    {
        var parsed = DateOnly.ParseExact(yearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (parsed, parsed.AddMonths(1).AddDays(-1));
    }

    /// <summary>The whole-day <see cref="DateTimeOffset"/> span (UTC midnight to UTC midnight) a
    /// month's date range covers — what <c>Transaction.Date</c> comparisons actually run against.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) OffsetRange(string yearMonth)
    {
        var (start, end) = Range(yearMonth);
        return (
            new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }

    /// <summary>D66: the month whose Transaction rows are purged when THIS month is approved — the
    /// calendar month immediately before it, regardless of whether a BudgetMonth row exists for it yet.</summary>
    public static string PreviousYearMonth(string yearMonth)
    {
        var (start, _) = Range(yearMonth);
        return YearMonthOf(start.AddMonths(-1));
    }

    /// <summary>Mockup 7's "4 days left" — the whole days remaining AFTER today in this month, zero
    /// once the month has ended (never negative).</summary>
    public static int DaysLeft(string yearMonth, DateOnly today)
    {
        var (_, end) = Range(yearMonth);
        return Math.Max(0, end.DayNumber - today.DayNumber);
    }

    public static string DisplayMonthName(string yearMonth)
    {
        var (start, _) = Range(yearMonth);
        return start.ToString("MMMM", CultureInfo.InvariantCulture);
    }
}
