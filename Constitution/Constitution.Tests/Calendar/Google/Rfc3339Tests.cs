using System.Globalization;
using Constitution.Infrastructure.Calendar;

namespace Constitution.Tests.Calendar.Google;

/// <summary>
/// An instant as Google Calendar's query parameters and event bodies want it: RFC 3339, with the offset as hours and minutes. The Google client's
/// own formatting of a <c>DateTimeOffset</c> writes the offset as hours alone (<c>-05</c>), which the API's documentation does not promise to accept;
/// the adapters therefore set the raw text.
/// </summary>
public class Rfc3339Tests
{
    [Theory]
    [InlineData(2026, 11, 2, 0, 0, 0, -5, 0, "2026-11-02T00:00:00-05:00")]
    [InlineData(2026, 3, 8, 6, 45, 30, -4, 0, "2026-03-08T06:45:30-04:00")]
    [InlineData(2026, 11, 3, 9, 0, 0, 0, 0, "2026-11-03T09:00:00+00:00")]
    [InlineData(2026, 11, 3, 9, 0, 0, 5, 30, "2026-11-03T09:00:00+05:30")]
    [InlineData(2026, 11, 3, 23, 59, 59, 9, 0, "2026-11-03T23:59:59+09:00")]
    public void AnInstantIsWrittenWithItsOffsetInHoursAndMinutes(int year, int month, int day, int hour, int minute, int second, int offsetHours, int offsetMinutes, string expected)
    {
        var offset = new TimeSpan(offsetHours, offsetHours < 0 ? -offsetMinutes : offsetMinutes, 0);

        Assert.Equal(expected, Rfc3339.Format(new DateTimeOffset(year, month, day, hour, minute, second, offset)));
    }

    [Fact]
    public void WhatIsWrittenReadsBackAsTheSameInstant()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4)).AddHours(1);

        var back = DateTimeOffset.Parse(Rfc3339.Format(instant), CultureInfo.InvariantCulture);

        Assert.Equal(instant, back);
        Assert.Equal(instant.Offset, back.Offset);
    }

    [Fact]
    public void ItDoesNotDependOnThePhonesCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal("2026-11-02T00:00:00-05:00", Rfc3339.Format(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5))));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
