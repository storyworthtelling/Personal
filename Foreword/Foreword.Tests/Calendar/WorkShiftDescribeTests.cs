using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The work-shift row on the Calendars screen, and the single-character format string that
/// crashed it on the owner's phone.
///
/// <para><c>TimeOnly.ToString("h")</c> throws <see cref="System.FormatException"/>: a
/// <b>one-character</b> format string is read as a <i>standard</i> specifier, and "h" is not one.
/// The default shift starts at 08:00, so <c>Describe()</c> threw for every shift the app ships
/// with — meaning the Calendars screen could not be opened at all, from a clean install, with no
/// data required to reproduce it. <c>TimeFormats.Hour12</c> already carried the "%h" fix and a
/// comment explaining it; this row hand-rolled its own copy and got it wrong, which is the reason
/// the formatting now goes through one place.</para>
/// </summary>
public class WorkShiftDescribeTests
{
    [Fact]
    public void TheDefaultShiftCanBeDescribed()
    {
        var shift = new WorkShift();

        Assert.Equal("Weekdays 8 – 4", shift.Describe());
    }

    [Fact]
    public void AnHourOnTheHourDropsItsMinutes()
    {
        var shift = new WorkShift { Start = "09:00", End = "17:00" };

        Assert.Equal("Weekdays 9 – 5", shift.Describe());
    }

    [Fact]
    public void AnHourWithMinutesKeepsThem()
    {
        var shift = new WorkShift { Start = "08:30", End = "15:45" };

        Assert.Equal("Weekdays 8:30 – 3:45", shift.Describe());
    }

    /// <summary>Noon and midnight are the two the 12-hour clock gets wrong if it divides.</summary>
    [Fact]
    public void NoonAndMidnightReadAsTwelve()
    {
        Assert.Equal("Weekdays 12 – 12", new WorkShift { Start = "00:00", End = "12:00" }.Describe());
    }

    /// <summary>
    /// A stored value that is not a time is shown back rather than thrown over — the row exists to
    /// let the owner see and fix what is stored.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotATimeIsShownBack()
    {
        var shift = new WorkShift { Start = "half eight", End = "16:00" };

        Assert.Equal("Weekdays half eight – 4", shift.Describe());
    }
}
