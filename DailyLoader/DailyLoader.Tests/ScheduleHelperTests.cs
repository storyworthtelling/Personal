using DailyLoader.Worker;
using Xunit;

namespace DailyLoader.Tests;

public class ScheduleHelperTests
{
    [Theory]
    [InlineData(2025, 6, 1)]   // Sunday, first of month
    [InlineData(2025, 3, 2)]   // Sunday, day 2
    [InlineData(2025, 2, 2)]   // Sunday, day 2
    [InlineData(2025, 1, 5)]   // Sunday, day 5
    [InlineData(2025, 4, 6)]   // Sunday, day 6
    [InlineData(2025, 5, 4)]   // Sunday, day 4
    public void IsFirstSundayOfMonth_ReturnsTrue_ForFirstSundayInMonth(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        Assert.True(ScheduleHelper.IsFirstSundayOfMonth(date));
    }

    [Theory]
    [InlineData(2025, 6, 8)]   // Sunday, second Sunday
    [InlineData(2025, 6, 15)]  // Sunday, third Sunday
    [InlineData(2025, 6, 2)]   // Monday, not Sunday
    [InlineData(2025, 6, 3)]   // Tuesday
    [InlineData(2025, 1, 6)]   // Monday
    public void IsFirstSundayOfMonth_ReturnsFalse_WhenNotFirstSunday(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        Assert.False(ScheduleHelper.IsFirstSundayOfMonth(date));
    }

    [Fact]
    public void NextDeliveryTime_ReturnsToday_WhenDeliveryTimeIsInFuture()
    {
        var timeZone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2025, 4, 14, 5, 0, 0, TimeSpan.Zero); // 05:00 UTC
        var deliveryTime = new TimeOnly(6, 0); // 06:00

        var next = ScheduleHelper.NextDeliveryTime(now, deliveryTime, timeZone);

        Assert.Equal(new DateTimeOffset(2025, 4, 14, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextDeliveryTime_ReturnsTomorrow_WhenDeliveryTimeHasPassed()
    {
        var timeZone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2025, 4, 14, 7, 0, 0, TimeSpan.Zero); // 07:00 UTC
        var deliveryTime = new TimeOnly(6, 0); // 06:00 already passed

        var next = ScheduleHelper.NextDeliveryTime(now, deliveryTime, timeZone);

        Assert.Equal(new DateTimeOffset(2025, 4, 15, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextDeliveryTime_AccountsForTimeZone()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        // 09:00 UTC = 05:00 EST; delivery is at 06:00 EST which hasn't happened yet
        var now = new DateTimeOffset(2025, 1, 14, 10, 0, 0, TimeSpan.Zero);
        var deliveryTime = new TimeOnly(6, 0);

        var next = ScheduleHelper.NextDeliveryTime(now, deliveryTime, timeZone);

        // 06:00 EST on 2025-01-14 = 11:00 UTC
        Assert.Equal(new DateTimeOffset(2025, 1, 14, 11, 0, 0, TimeSpan.Zero), next.ToUniversalTime());
    }
}
