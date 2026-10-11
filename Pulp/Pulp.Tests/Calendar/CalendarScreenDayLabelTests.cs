using Pulp.Domain.Calendar;
using Pulp.Domain.Core;

namespace Pulp.Tests.Calendar;

/// <summary>
/// Mockup 8's day headings, in the relative-time rule (DESIGN.md §11, D148): "Tonight" for a day that
/// has an evening item, "Today" otherwise, "Tomorrow", a weekday within the week, then the date. Read in
/// the viewer's zone with the clock an argument. A forward-looking cousin of <c>RecentDayLabel</c>, which
/// only looks back.
/// </summary>
public class CalendarScreenDayLabelTests
{
    // A Wednesday morning, invented: 7 October 2026, 09:00 in a zone five hours behind UTC.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static CalendarItem Item(DateOnly day, TimeOnly? time) => new(
        "item:" + day.DayNumber + ":" + time,
        CalendarItemKind.Plan,
        new CalendarWhen(day, time),
        "The Majestic",
        null,
        null,
        new TitleId("the-majestic"),
        null,
        [],
        []);

    private static string Label(DateOnly day, TimeOnly? time) =>
        CalendarScreenDayLabel.For(new CalendarDay(day, [Item(day, time)]), Now, Zone);

    [Fact]
    public void TodayWithAnEveningItemIsTonight()
    {
        Assert.Equal("Tonight", Label(Today, new TimeOnly(20, 0)));
    }

    [Fact]
    public void TodayWithOnlyAMorningOrAllDayItemIsToday()
    {
        Assert.Equal("Today", Label(Today, new TimeOnly(9, 0)));
        Assert.Equal("Today", Label(Today, null));
    }

    [Fact]
    public void TheNextDayIsTomorrow()
    {
        Assert.Equal("Tomorrow", Label(Today.AddDays(1), null));
    }

    [Theory]
    [InlineData(2, "Friday")]
    [InlineData(6, "Tuesday")]
    public void WithinTheWeekIsTheWeekday(int daysAhead, string expected)
    {
        Assert.Equal(expected, Label(Today.AddDays(daysAhead), null));
    }

    [Fact]
    public void AWeekAheadOrMoreIsTheDate()
    {
        Assert.Equal("Oct 14", Label(Today.AddDays(7), null));
    }

    [Fact]
    public void AnotherYearsDateCarriesTheYear()
    {
        var late = new DateTimeOffset(2026, 12, 20, 14, 0, 0, TimeSpan.Zero);
        var day = new DateOnly(2027, 1, 5);
        Assert.Equal("Jan 5, 2027", CalendarScreenDayLabel.For(new CalendarDay(day, [Item(day, null)]), late, Zone));
    }

    [Fact]
    public void TodayIsReadInTheViewersZoneNotUtc()
    {
        // 03:00 UTC on the 8th is still the evening of the 7th five hours behind.
        var lateUtc = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal("Tonight", CalendarScreenDayLabel.For(new CalendarDay(Today, [Item(Today, new TimeOnly(21, 0))]), lateUtc, Zone));
    }
}
