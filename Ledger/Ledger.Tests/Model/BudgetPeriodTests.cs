using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

public class BudgetPeriodTests
{
    [Fact]
    public void Range_ReturnsTheFirstAndLastCalendarDay()
    {
        var (start, end) = BudgetPeriod.Range("2026-09");
        Assert.Equal(new DateOnly(2026, 9, 1), start);
        Assert.Equal(new DateOnly(2026, 9, 30), end);
    }

    [Fact]
    public void PreviousYearMonth_StepsBackOneCalendarMonth()
    {
        Assert.Equal("2026-08", BudgetPeriod.PreviousYearMonth("2026-09"));
    }

    [Fact]
    public void PreviousYearMonth_CrossesAYearBoundary()
    {
        Assert.Equal("2025-12", BudgetPeriod.PreviousYearMonth("2026-01"));
    }

    [Fact]
    public void DaysLeft_CountsWholeDaysAfterToday_NeverNegative()
    {
        Assert.Equal(4, BudgetPeriod.DaysLeft("2026-09", new DateOnly(2026, 9, 26)));
        Assert.Equal(0, BudgetPeriod.DaysLeft("2026-09", new DateOnly(2026, 9, 30)));
        Assert.Equal(0, BudgetPeriod.DaysLeft("2026-09", new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void YearMonthOf_FormatsAsYyyyMm()
    {
        Assert.Equal("2026-09", BudgetPeriod.YearMonthOf(new DateOnly(2026, 9, 5)));
    }
}
