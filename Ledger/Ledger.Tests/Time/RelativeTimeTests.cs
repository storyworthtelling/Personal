using Ledger.Domain.Time;

namespace Ledger.Tests.Time;

// The list-timestamp voice (DESIGN.md §11), shared verbatim across the repo's apps and pinned here so
// Ledger's copy can't drift.
public class RelativeTimeTests
{
    private static readonly DateTime Now = new(2026, 7, 4, 15, 30, 0); // Saturday

    [Theory]
    [InlineData(0, 0, "just now")]
    [InlineData(0, 45, "just now")]
    [InlineData(5, 0, "5m ago")]
    [InlineData(59, 0, "59m ago")]
    public void MinutesAndSeconds(int minutesAgo, int secondsAgo, string expected)
    {
        var timestamp = Now.AddMinutes(-minutesAgo).AddSeconds(-secondsAgo);
        Assert.Equal(expected, RelativeTime.Format(timestamp, Now));
    }

    [Fact]
    public void HoursWithinTheSameDay()
    {
        Assert.Equal("2h ago", RelativeTime.Format(Now.AddHours(-2), Now));
    }

    [Fact]
    public void YesterdayByCalendarNotByElapsedHours()
    {
        var lateLastNight = new DateTime(2026, 7, 3, 23, 30, 0);
        Assert.Equal("yesterday", RelativeTime.Format(lateLastNight, Now));
    }

    [Fact]
    public void WithinTheWeekUsesTheDayName()
    {
        Assert.Equal("Tuesday", RelativeTime.Format(new DateTime(2026, 6, 30, 12, 0, 0), Now));
    }

    [Fact]
    public void OlderThisYearUsesMonthDay()
    {
        Assert.Equal("Jun 20", RelativeTime.Format(new DateTime(2026, 6, 20, 12, 0, 0), Now));
    }

    [Fact]
    public void PreviousYearsCarryTheYear()
    {
        Assert.Equal("Jun 30, 2025", RelativeTime.Format(new DateTime(2025, 6, 30, 12, 0, 0), Now));
    }
}

public class TimeFormatsTests
{
    [Theory]
    [InlineData(19, 42, "7:42 pm")]
    [InlineData(7, 5, "7:05 am")]
    [InlineData(0, 15, "12:15 am")]
    [InlineData(12, 0, "12:00 pm")]
    public void Clock12IsLowercaseWithoutLeadingZero(int hour, int minute, string expected)
    {
        Assert.Equal(expected, TimeFormats.Clock12(new DateTime(2026, 7, 4, hour, minute, 0)));
    }

    [Fact]
    public void StampCombinesDateAndClock()
    {
        Assert.Equal("Jun 30, 7:42 pm", TimeFormats.Stamp(new DateTime(2026, 6, 30, 19, 42, 0)));
    }
}
