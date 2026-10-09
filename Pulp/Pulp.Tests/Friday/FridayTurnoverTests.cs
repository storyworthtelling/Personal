using Pulp.Domain.Friday;

namespace Pulp.Tests.Friday;

/// <summary>D110: the stack turns over at Friday 6 am local — local, so a clock change moves it with the household.</summary>
public class FridayTurnoverTests
{
    private static readonly TimeZoneInfo Harbor =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    [Fact]
    public void MidweekBelongsToLastFridaysStack()
    {
        // Wednesday 14 Oct 2026, noon local.
        var wednesday = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.FromHours(-5)), FridayTurnover.Current(wednesday, Harbor));
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.FromHours(-5)), FridayTurnover.Next(wednesday, Harbor));
    }

    [Fact]
    public void FridayBeforeSixIsStillLastWeek()
    {
        var early = new DateTimeOffset(2026, 10, 16, 5, 59, 0, TimeSpan.FromHours(-5));

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.FromHours(-5)), FridayTurnover.Current(early, Harbor));
    }

    [Fact]
    public void SixOnFridayIsTheNewWeek()
    {
        var six = new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(six, FridayTurnover.Current(six, Harbor));
        Assert.Equal(six.AddDays(7), FridayTurnover.Next(six, Harbor));
    }

    [Fact]
    public void TheInstantIsReadInTheHouseholdsZoneNotTheOffsetItArrivedIn()
    {
        // 10:30 UTC on Friday is 5:30 am in the Harbor zone: not yet the new week.
        var utc = new DateTimeOffset(2026, 10, 16, 10, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.FromHours(-5)), FridayTurnover.Current(utc, Harbor));
    }

    [Fact]
    public void AClockChangeKeepsTheTurnoverAtSixLocal()
    {
        // A zone with daylight saving (ends on the first Sunday of November): the week across it
        // is a week of six-am Fridays, not 168 hours.
        var zone = InventedDaylightZone();
        var before = new DateTimeOffset(2026, 10, 30, 6, 0, 0, TimeSpan.FromHours(-4));

        var next = FridayTurnover.Next(before, zone);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.FromHours(-5)), next);
    }

    /// <summary>An invented zone five hours behind UTC, an hour less from March's second Sunday to November's first.</summary>
    private static TimeZoneInfo InventedDaylightZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone(
            "Invented/Daylight", TimeSpan.FromHours(-5), "Invented Daylight", "Invented Standard", "Invented Daylight", [rule]);
    }
}
