using DailyLoader.Domain.Models;
using DailyLoader.Infrastructure.Google;
using Xunit;

namespace DailyLoader.Tests.Google;

public class RecurrenceParserTests
{
    [Fact]
    public void Parse_NullReturnsNone()
    {
        var (freq, count) = RecurrenceParser.Parse(null);
        Assert.Equal(RecurrenceFrequency.None, freq);
        Assert.Null(count);
    }

    [Fact]
    public void Parse_WeeklyWithCount()
    {
        var (freq, count) = RecurrenceParser.Parse(["RRULE:FREQ=WEEKLY;COUNT=4"]);
        Assert.Equal(RecurrenceFrequency.Weekly, freq);
        Assert.Equal(4, count);
    }

    [Fact]
    public void Parse_DailyOpenEnded()
    {
        var (freq, count) = RecurrenceParser.Parse(["RRULE:FREQ=DAILY"]);
        Assert.Equal(RecurrenceFrequency.Daily, freq);
        Assert.Null(count);
    }

    [Fact]
    public void Parse_MonthlyWithByDay()
    {
        var (freq, _) = RecurrenceParser.Parse(["RRULE:FREQ=MONTHLY;BYDAY=2SU"]);
        Assert.Equal(RecurrenceFrequency.Monthly, freq);
    }

    [Fact]
    public void Parse_Yearly()
    {
        var (freq, _) = RecurrenceParser.Parse(["RRULE:FREQ=YEARLY"]);
        Assert.Equal(RecurrenceFrequency.Yearly, freq);
    }

    [Fact]
    public void Parse_IgnoresNonRruleLines()
    {
        var (freq, count) = RecurrenceParser.Parse(["EXDATE:20260101T000000Z", "RRULE:FREQ=WEEKLY;COUNT=10"]);
        Assert.Equal(RecurrenceFrequency.Weekly, freq);
        Assert.Equal(10, count);
    }
}
