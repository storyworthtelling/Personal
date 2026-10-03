using System.Globalization;
using Index.Domain.Model;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// The words a printed worksheet or follow-up page uses for months and dates, derived from the month
/// the page is for and always in the invariant culture — the page is English whatever the phone that
/// built it is set to.
/// </summary>
public static class WorksheetDates
{
    public static string MonthYear(string yearMonth) =>
        Parse(yearMonth).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string MonthWord(string yearMonth) =>
        Parse(yearMonth).ToString("MMMM", CultureInfo.InvariantCulture);

    public static string PreviousMonthWord(string yearMonth) => MonthWord(BudgetPeriod.PreviousYearMonth(yearMonth));

    public static string PreviousMonthShort(string yearMonth)
    {
        var word = PreviousMonthWord(yearMonth);
        return word switch
        {
            "September" => "Sept",
            _ when word.Length <= 4 => word,
            _ => word[..3],
        };
    }

    public static string PrintedOn(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);

    private static DateOnly Parse(string yearMonth) =>
        DateOnly.ParseExact(yearMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
