using Index.Domain.Model;
using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

public class WorksheetDeliveryScheduleTests
{
    [Fact]
    public void IsDueToday_OnlyOnTheLastCalendarDayOfTheMonth()
    {
        Assert.False(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2026, 9, 29)));
        Assert.True(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2026, 9, 30))); // September has 30 days
        Assert.False(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2026, 10, 30)));
        Assert.True(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2026, 10, 31))); // October has 31 days
    }

    [Fact]
    public void IsDueToday_HandlesFebruaryInBothLeapAndNonLeapYears()
    {
        Assert.True(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2026, 2, 28))); // 2026: not a leap year
        Assert.True(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2024, 2, 29))); // 2024: a leap year
        Assert.False(WorksheetDeliverySchedule.IsDueToday(new DateOnly(2024, 2, 28)));
    }

    [Fact]
    public void TargetYearMonth_IsAlwaysNextMonth()
    {
        Assert.Equal("2026-10", WorksheetDeliverySchedule.TargetYearMonth(new DateOnly(2026, 9, 30)));
        Assert.Equal("2027-01", WorksheetDeliverySchedule.TargetYearMonth(new DateOnly(2026, 12, 31)));
    }

    // ---- Guard R-G13: the first read-back of a delivered worksheet ----

    [Theory]
    [InlineData("2026-09-29", false)] // printed Sep 30 - not even printed yet
    [InlineData("2026-09-30", false)] // the day it is delivered: nothing to read yet
    [InlineData("2026-10-01", true)]  // the morning after
    [InlineData("2026-10-02", true)]  // and every morning after that, until it has been read
    [InlineData("2026-10-31", true)]
    public void IsFirstReadBackDue_FromTheMorningAfterTheSheetIsPrinted(string today, bool expected)
    {
        var day = DateOnly.ParseExact(today, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, WorksheetDeliverySchedule.IsFirstReadBackDue(day, "2026-10", BudgetMonthStatus.DeliveredToRemarkable));
    }

    [Theory]
    [InlineData(BudgetMonthStatus.Draft)]
    [InlineData(BudgetMonthStatus.AwaitingConfirmation)]
    [InlineData(BudgetMonthStatus.Approved)]
    public void IsFirstReadBackDue_NeverForASheetThatIsNotWaitingToBeRead(BudgetMonthStatus status)
    {
        // Read once: any status past "delivered" means it has been, so the sheet's ink is never put
        // back over a correction made since.
        Assert.False(WorksheetDeliverySchedule.IsFirstReadBackDue(new DateOnly(2026, 10, 15), "2026-10", status));
    }

    [Fact]
    public void IsFirstReadBackDue_IsNotTheLastDayOnlyRule()
    {
        // The rule it replaces: a sheet is read on the last day of its own month. Oct 1 is not that day.
        var oct1 = new DateOnly(2026, 10, 1);

        Assert.False(WorksheetDeliverySchedule.IsDueToday(oct1));
        Assert.True(WorksheetDeliverySchedule.IsFirstReadBackDue(oct1, "2026-10", BudgetMonthStatus.DeliveredToRemarkable));
    }
}
