using Constitution.Domain.Time;

namespace Constitution.Tests.Time;

public class ConstitutionTimeFormatsTests
{
    [Fact]
    public void HeaderDateReadsWeekdayAbbreviatedMonthAndDay()
    {
        Assert.Equal("Tuesday, Nov 17", TodayHeader.Date(new DateTime(2026, 11, 17)));
    }

    [Theory]
    [InlineData(5, 30, "5:30")]
    [InlineData(6, 15, "6:15")]
    public void TimesReadInTwelveHourFormWithoutLeadingZero(int hour, int minute, string expected)
    {
        Assert.Equal(expected, TimeFormats.ClockShort(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void AfternoonTimesDropTheTwentyFourHourForm()
    {
        Assert.Equal("5:30", TimeFormats.ClockShort(new TimeOnly(17, 30)));
        Assert.Equal("5:30 pm", TimeFormats.Clock12(new DateTime(2026, 11, 17, 17, 30, 0)));
    }

    [Fact]
    public void RelativeTimeIsAvailable()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0);
        Assert.Equal("just now", RelativeTime.Format(now.AddSeconds(-10), now));
    }
}
