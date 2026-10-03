namespace Index.Domain.Model;

/// <summary>
/// The month cell of a tracking-only history row (mockup 8 frame 2, <c>.mh-m</c>): the month's name, with
/// a small "so far" line BENEATH it for the month in progress. A month from another year is labelled the
/// way the charts' axes are ("Sep '25", <see cref="ChartAxis.MonthYearLabel"/>), because a bare "September"
/// two rows below a bare "September" is ambiguous the moment the list crosses a year boundary.
/// </summary>
public readonly record struct HistoryRowText(string Month, string? SubLine)
{
    public static HistoryRowText For(string yearMonth, bool isCurrentMonth, int currentYear)
    {
        var (start, _) = BudgetPeriod.Range(yearMonth);
        var month = start.Year == currentYear ? BudgetPeriod.DisplayMonthName(yearMonth) : ChartAxis.MonthYearLabel(start);
        return new HistoryRowText(month, isCurrentMonth ? "so far" : null);
    }
}
