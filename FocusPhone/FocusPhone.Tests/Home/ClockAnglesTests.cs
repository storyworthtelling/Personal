using FocusPhone.Domain.Home;
using Xunit;

namespace FocusPhone.Tests.Home;

public class ClockAnglesTests
{
    [Theory]
    [InlineData(12, 0, 0f, 0f)]
    [InlineData(3, 0, 90f, 0f)]
    [InlineData(6, 0, 180f, 0f)]
    [InlineData(9, 0, 270f, 0f)]
    [InlineData(18, 30, 195f, 180f)] // 24h input wraps; hour hand advances with minutes
    public void FromTime_MapsHoursAndMinutes(int hour, int minute, float expectedHour, float expectedMinute)
    {
        var (hourDegrees, minuteDegrees) = ClockAngles.FromTime(new TimeSpan(hour, minute, 0));
        Assert.Equal(expectedHour, hourDegrees, precision: 3);
        Assert.Equal(expectedMinute, minuteDegrees, precision: 3);
    }

    [Fact]
    public void HourHand_CreepsWithinTheHour()
    {
        var atTen = ClockAngles.FromTime(new TimeSpan(7, 0, 0)).HourDegrees;
        var atTenThirty = ClockAngles.FromTime(new TimeSpan(7, 30, 0)).HourDegrees;
        Assert.Equal(atTen + 15f, atTenThirty, precision: 3);
    }

    [Fact]
    public void MinuteHand_AdvancesWithSeconds()
    {
        var whole = ClockAngles.FromTime(new TimeSpan(0, 7, 42, 0)).MinuteDegrees;
        var half = ClockAngles.FromTime(new TimeSpan(0, 7, 42, 30)).MinuteDegrees;
        Assert.True(half > whole);
    }
}
