using Pulp.Domain.Calendar;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The daily pass is first run in the small hours (D149): the first run waits until the next 03:00 in the
/// household's own zone, then repeats every day. The delay is read off the clock handed in.
/// </summary>
public class CalendarScreenScheduleTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void AfternoonWaitsUntilThreeTomorrow()
    {
        Assert.Equal(TimeSpan.FromHours(13), CalendarScreenSchedule.DelayUntilNextRun(Local(7, 14), Zone));
    }

    [Fact]
    public void BeforeThreeWaitsUntilThreeToday()
    {
        Assert.Equal(TimeSpan.FromHours(1), CalendarScreenSchedule.DelayUntilNextRun(Local(7, 2), Zone));
    }

    [Fact]
    public void ExactlyThreeWaitsAFullDayRatherThanRunningAtOnce()
    {
        Assert.Equal(TimeSpan.FromHours(24), CalendarScreenSchedule.DelayUntilNextRun(Local(7, 3), Zone));
    }

    [Fact]
    public void TheClockIsReadInTheZoneNotInUtc()
    {
        // 07:30 UTC is 02:30 local: half an hour to go, not twenty-two.
        var utc = new DateTimeOffset(2026, 10, 7, 7, 30, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(30), CalendarScreenSchedule.DelayUntilNextRun(utc, Zone));
    }

    [Fact]
    public void TheRepeatIsOneDay()
    {
        Assert.Equal(TimeSpan.FromHours(24), CalendarScreenSchedule.Repeat);
    }
}
