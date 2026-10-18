using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// Turning "6 pm on that date" into an instant. Everything that is a time of day rather than an
/// instant (work hours, dinner, the bedtime routine, the start of a local day) goes through here,
/// so the two awkward nights of the year are settled once and pinned.
///
/// <para>Spring forward: 2 am Sunday 8 March 2026 becomes 3 am, so 2:30 am does not exist. Fall
/// back: 2 am Sunday 1 November 2026 becomes 1 am, so 1:30 am happens twice. The rule that keeps a
/// planner honest is that a span is never made shorter than the wall clock says, and a gap resolves
/// to the moment the clock jumps.</para>
/// </summary>
public class ZoneClockTests
{
    private static readonly DateOnly SpringForward = new(2026, 3, 8);
    private static readonly DateOnly FallBack = new(2026, 11, 1);

    [Fact]
    public void AnOrdinaryTimeTakesTheOffsetInForce()
    {
        var summer = ZoneClock.At(new DateOnly(2026, 10, 26), new TimeOnly(18, 0), Zone, ClockEdge.Start);
        var winter = ZoneClock.At(new DateOnly(2026, 12, 7), new TimeOnly(18, 0), Zone, ClockEdge.Start);

        Assert.Equal(Edt(10, 26, 18), summer);
        Assert.Equal(TimeSpan.FromHours(-4), summer.Offset);
        Assert.Equal(Est(12, 7, 18), winter);
        Assert.Equal(TimeSpan.FromHours(-5), winter.Offset);
    }

    [Theory]
    [InlineData(ClockEdge.Start)]
    [InlineData(ClockEdge.End)]
    public void ASpringGapTimeResolvesToTheJump(ClockEdge edge)
    {
        // 2:30 am never happens. The clock goes 1:59 EST to 3:00 EDT, so the nearest real moment
        // is 3:00 EDT, which is also 2:00 EST.
        var instant = ZoneClock.At(SpringForward, new TimeOnly(2, 30), Zone, edge);

        Assert.Equal(Edt(3, 8, 3), instant);
        Assert.Equal(TimeSpan.FromHours(-4), instant.Offset);
    }

    [Fact]
    public void TheFirstMomentOfTheGapIsTheJumpToo()
    {
        Assert.Equal(Edt(3, 8, 3), ZoneClock.At(SpringForward, new TimeOnly(2, 0), Zone, ClockEdge.Start));
    }

    [Fact]
    public void JustBeforeTheGapIsOrdinary()
    {
        var instant = ZoneClock.At(SpringForward, new TimeOnly(1, 59), Zone, ClockEdge.Start);

        Assert.Equal(Est(3, 8, 1, 59), instant);
    }

    [Fact]
    public void ASpanOpensAtTheEarlierOfAnAmbiguousTime()
    {
        // 1:30 am happens twice on the fall-back night: once on EDT, once on EST.
        var opens = ZoneClock.At(FallBack, new TimeOnly(1, 30), Zone, ClockEdge.Start);

        Assert.Equal(Edt(11, 1, 1, 30), opens);
        Assert.Equal(TimeSpan.FromHours(-4), opens.Offset);
    }

    [Fact]
    public void ASpanClosesAtTheLaterOfAnAmbiguousTime()
    {
        var closes = ZoneClock.At(FallBack, new TimeOnly(1, 30), Zone, ClockEdge.End);

        Assert.Equal(Est(11, 1, 1, 30), closes);
        Assert.Equal(TimeSpan.FromHours(-5), closes.Offset);
    }

    [Fact]
    public void ASpanOverTheRepeatedHourKeepsBothPasses()
    {
        var opens = ZoneClock.At(FallBack, new TimeOnly(1, 15), Zone, ClockEdge.Start);
        var closes = ZoneClock.At(FallBack, new TimeOnly(1, 45), Zone, ClockEdge.End);

        // The wall clock says half an hour; both 1 o'clock hours are covered, so it is 1.5 hours.
        Assert.Equal(TimeSpan.FromMinutes(90), closes - opens);
    }

    [Fact]
    public void ATimeJustAfterTheAmbiguousHourIsOrdinary()
    {
        Assert.Equal(Est(11, 1, 2, 30), ZoneClock.At(FallBack, new TimeOnly(2, 30), Zone, ClockEdge.Start));
    }

    [Fact]
    public void ASpringForwardDayIsTwentyThreeHours()
    {
        var start = ZoneClock.StartOfDay(SpringForward, Zone);
        var next = ZoneClock.StartOfDay(SpringForward.AddDays(1), Zone);

        Assert.Equal(Est(3, 8, 0), start);
        Assert.Equal(TimeSpan.FromHours(23), next - start);
    }

    [Fact]
    public void AFallBackDayIsTwentyFiveHours()
    {
        var start = ZoneClock.StartOfDay(FallBack, Zone);
        var next = ZoneClock.StartOfDay(FallBack.AddDays(1), Zone);

        Assert.Equal(Edt(11, 1, 0), start);
        Assert.Equal(TimeSpan.FromHours(25), next - start);
    }

    [Fact]
    public void TheDayIsLocalNotUtc()
    {
        // 11 pm on Sunday evening in New York is already Monday in UTC.
        var lateSunday = new DateTimeOffset(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 10, 25), ZoneClock.DayOf(lateSunday, Zone));
    }

    [Fact]
    public void LocalMidnightStartsTheNewDay()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), ZoneClock.DayOf(Edt(10, 26, 0), Zone));
        Assert.Equal(new DateOnly(2026, 10, 25), ZoneClock.DayOf(Edt(10, 25, 23, 59), Zone));
    }

    [Fact]
    public void InZoneKeepsTheInstantAndChangesTheOffset()
    {
        var utc = new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);

        var local = ZoneClock.InZone(utc, Zone);

        Assert.Equal(utc, local);
        Assert.Equal(TimeSpan.FromHours(-4), local.Offset);
        Assert.Equal(10, local.Hour);
    }

    [Fact]
    public void ASkippedMidnightStartsLater()
    {
        // Some zones put the clock change at midnight, so midnight itself can be skipped. A zone
        // that jumps from 23:59 to 01:00 has no 00:30.
        var skipsMidnight = TimeZoneInfo.CreateCustomTimeZone(
            "Invented Midnight",
            TimeSpan.FromHours(-3),
            "Invented Midnight",
            "Invented Midnight Standard",
            "Invented Midnight Daylight",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date,
                    DateTime.MaxValue.Date,
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1, DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 2, 3, DayOfWeek.Sunday)),
            ]);

        var start = ZoneClock.StartOfDay(new DateOnly(2026, 11, 1), skipsMidnight);

        // The day's first real moment is 1:00 am local (UTC-2 on daylight time).
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(-2)), start);
    }
}
