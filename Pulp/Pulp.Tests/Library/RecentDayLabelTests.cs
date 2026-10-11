using Pulp.Domain.Library;

namespace Pulp.Tests.Library;

/// <summary>
/// Mockup 05's day headings, in the relative-time rule (DESIGN.md §11, D226): "Tonight" for this
/// evening, "Yesterday", a weekday within the week, then the date — with the year only when it is not
/// this one. Read in the viewer's own zone, with the clock an argument.
/// </summary>
public class RecentDayLabelTests
{
    // A Thursday evening, invented: 12 March 2026, 21:00 in a zone five hours behind UTC.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("example-minus-five", TimeSpan.FromHours(-5), "Example", "Example");
    private static readonly DateTimeOffset Now = new(2026, 3, 13, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 12);

    private static DateTimeOffset LocalAt(DateOnly day, int hour) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(-5));

    [Fact]
    public void ThisEveningIsTonight()
    {
        Assert.Equal("Tonight", RecentDayLabel.For(Today, LocalAt(Today, 20), Now, Zone));
    }

    [Fact]
    public void ThisMorningIsToday()
    {
        Assert.Equal("Today", RecentDayLabel.For(Today, LocalAt(Today, 9), Now, Zone));
    }

    [Fact]
    public void TheDayBeforeIsYesterday()
    {
        var yesterday = Today.AddDays(-1);
        Assert.Equal("Yesterday", RecentDayLabel.For(yesterday, LocalAt(yesterday, 21), Now, Zone));
    }

    [Theory]
    [InlineData(2, "Tuesday")]
    [InlineData(6, "Friday")]
    public void WithinTheWeekIsTheWeekday(int daysBack, string expected)
    {
        var day = Today.AddDays(-daysBack);
        Assert.Equal(expected, RecentDayLabel.For(day, LocalAt(day, 20), Now, Zone));
    }

    [Fact]
    public void AWeekOrMoreAgoIsTheDate()
    {
        var day = Today.AddDays(-7);
        Assert.Equal("Mar 5", RecentDayLabel.For(day, LocalAt(day, 20), Now, Zone));
    }

    [Fact]
    public void AnotherYearCarriesItsYear()
    {
        var day = new DateOnly(2025, 9, 27);
        Assert.Equal("Sep 27, 2025", RecentDayLabel.For(day, LocalAt(day, 20), Now, Zone));
    }

    [Fact]
    public void TodayIsReadInTheViewersZoneNotUtc()
    {
        // 02:00 UTC on the 13th is still the evening of the 12th here, so the 12th is tonight.
        Assert.Equal("Tonight", RecentDayLabel.For(Today, LocalAt(Today, 20), Now, Zone));
        Assert.Equal("Yesterday", RecentDayLabel.For(Today.AddDays(-1), LocalAt(Today.AddDays(-1), 20), Now, Zone));
    }

    [Fact]
    public void ASittingStampedAheadOfTheClockReadsAsToday()
    {
        var tomorrow = Today.AddDays(1);
        Assert.Equal("Today", RecentDayLabel.For(tomorrow, LocalAt(tomorrow, 9), Now, Zone));
    }
}
