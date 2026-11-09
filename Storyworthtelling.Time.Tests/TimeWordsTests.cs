using Storyworthtelling.Time;

namespace Storyworthtelling.Time.Tests;

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
        // 16 hours ago but across midnight: reads "yesterday", never "16h ago".
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
    public void DateLongReadsLikeAChapterDate()
    {
        Assert.Equal("Saturday, July 4", TimeFormats.DateLong(new DateTime(2026, 7, 4)));
    }

    [Fact]
    public void StampCombinesDateAndClock()
    {
        Assert.Equal("Jun 30, 7:42 pm", TimeFormats.Stamp(new DateTime(2026, 6, 30, 19, 42, 0)));
    }
}

public class ClockShortAndHourTests
{
    [Theory]
    [InlineData(5, 30, "5:30")]
    [InlineData(17, 30, "5:30")]
    [InlineData(8, 0, "8")]
    [InlineData(12, 0, "12")]
    public void ClockShortCarriesMinutesOnlyWhenItHasAny(int hour, int minute, string expected)
    {
        Assert.Equal(expected, TimeFormats.ClockShort(new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(15, "3pm")]
    [InlineData(10, "10am")]
    [InlineData(12, "12pm")]
    public void Hour12IsTheHourAlone(int hour, string expected)
    {
        Assert.Equal(expected, TimeFormats.Hour12(new DateTime(2026, 7, 4, hour, 0, 0)));
    }
}

/// <summary>
/// Pulp keeps instants and the viewer's zone rather than local DateTimes, and printed its own "5 min ago" /
/// "3 h ago" where the suite says "5m ago" / "3h ago" (audit 01 §13). The overloads convert first, then say it
/// the suite's way.
/// </summary>
public class ZoneAwareTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.CreateCustomTimeZone("test-pacific", TimeSpan.FromHours(-8), "test", "test");

    [Fact]
    public void AnInstantIsReadInTheViewersZone()
    {
        // 02:00 UTC on Jan 6 is the evening of Jan 5 in UTC-8.
        var at = new DateTimeOffset(2026, 1, 6, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal("6:00 pm", TimeFormats.Clock12(at, Pacific));
    }

    [Fact]
    public void RelativeTimeUsesTheSuitesWordsAndTheViewersDays()
    {
        var now = new DateTimeOffset(2026, 1, 6, 2, 0, 0, TimeSpan.Zero);  // Jan 5, 18:00 in UTC-8
        Assert.Equal("5m ago", RelativeTime.Format(now.AddMinutes(-5), now, Pacific));
        Assert.Equal("3h ago", RelativeTime.Format(now.AddHours(-3), now, Pacific));
        // 20:00 UTC on Jan 5 is noon in UTC-8: the same day there, whatever UTC says.
        Assert.Equal("6h ago", RelativeTime.Format(new DateTimeOffset(2026, 1, 5, 20, 0, 0, TimeSpan.Zero), now, Pacific));
        Assert.Equal("yesterday", RelativeTime.Format(now.AddHours(-20), now, Pacific));
    }
}
