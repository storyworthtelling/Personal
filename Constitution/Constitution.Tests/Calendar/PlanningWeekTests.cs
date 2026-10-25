using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// The household's planning week runs Sunday through Saturday, and this is the one place that says so. Dates in these
/// tests are written out: 25 October 2026 is a Sunday, and the clocks go back at 2 am on Sunday 1 November.
/// </summary>
public class PlanningWeekTests
{
    private static readonly DateOnly Sunday = new(2026, 10, 25);

    [Fact]
    public void TheWeekStartsOnSunday()
    {
        Assert.Equal(DayOfWeek.Sunday, PlanningWeek.FirstDay);
        Assert.Equal(7, PlanningWeek.DaysInWeek);
    }

    [Fact]
    public void ASundayStartsItsOwnWeek()
    {
        Assert.Equal(Sunday, PlanningWeek.StartOf(Sunday));
        Assert.True(PlanningWeek.IsStart(Sunday));
    }

    [Fact]
    public void EveryDayOfTheWeekFindsItsSunday()
    {
        for (var offset = 0; offset < 7; offset++)
        {
            Assert.Equal(Sunday, PlanningWeek.StartOf(Sunday.AddDays(offset)));
        }

        Assert.Equal(Sunday.AddDays(7), PlanningWeek.StartOf(Sunday.AddDays(7)));
    }

    [Fact]
    public void MondayIsNotAStart()
    {
        Assert.False(PlanningWeek.IsStart(Sunday.AddDays(1)));
        Assert.Equal(Sunday, PlanningWeek.StartOf(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void OnlySundaysAreStarts()
    {
        var starts = Enumerable.Range(0, 28).Select(offset => Sunday.AddDays(offset)).Where(PlanningWeek.IsStart).ToList();

        Assert.Equal([Sunday, Sunday.AddDays(7), Sunday.AddDays(14), Sunday.AddDays(21)], starts);
    }

    [Fact]
    public void TheWeekEndsOnTheFollowingSaturday()
    {
        var end = PlanningWeek.EndOf(Sunday);

        Assert.Equal(new DateOnly(2026, 10, 31), end);
        Assert.Equal(DayOfWeek.Saturday, end.DayOfWeek);
        Assert.Equal(PlanningWeek.EndOf(Sunday), PlanningWeek.EndOf(Sunday.AddDays(3)));
    }

    [Fact]
    public void ThereAreSevenDaysSundayFirst()
    {
        var days = PlanningWeek.Days(Sunday);

        Assert.Equal(7, days.Count);
        Assert.Equal(Sunday, days[0]);
        Assert.Equal(DayOfWeek.Sunday, days[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, days[6].DayOfWeek);
        Assert.Equal(Enumerable.Range(0, 7).Select(Sunday.AddDays), days);
    }

    [Fact]
    public void PositionCountsFromSunday()
    {
        Assert.Equal(0, PlanningWeek.Position(DayOfWeek.Sunday));
        Assert.Equal(1, PlanningWeek.Position(DayOfWeek.Monday));
        Assert.Equal(6, PlanningWeek.Position(DayOfWeek.Saturday));
        Assert.Equal(3, PlanningWeek.Position(new DateOnly(2026, 10, 28)));
    }

    [Fact]
    public void ADayThatIsNotAStartIsRefusedWithWords()
    {
        var thrown = Assert.Throws<ArgumentException>(() => PlanningWeek.RequireStart(new DateOnly(2026, 10, 26), "weekStart"));

        Assert.Contains("Sunday", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Monday", thrown.Message, StringComparison.Ordinal);
        Assert.Equal("weekStart", thrown.ParamName);
        PlanningWeek.RequireStart(Sunday, "weekStart");
        Assert.Throws<ArgumentException>(() => PlanningWeek.Days(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void TheIndexRisesByOneAWeek()
    {
        var index = PlanningWeek.IndexOf(Sunday);
        for (var week = 1; week <= 160; week++)
        {
            Assert.Equal(index + week, PlanningWeek.IndexOf(Sunday.AddDays(week * 7)));
        }
    }

    [Fact]
    public void EveryDayOfAWeekHasOneIndex()
    {
        var index = PlanningWeek.IndexOf(Sunday);
        for (var offset = 0; offset < 7; offset++)
        {
            Assert.Equal(index, PlanningWeek.IndexOf(Sunday.AddDays(offset)));
        }

        Assert.Equal(index + 1, PlanningWeek.IndexOf(Sunday.AddDays(7)));
    }

    [Fact]
    public void AnIndexNamesItsSundayAndBack()
    {
        for (var week = 0; week < 200; week++)
        {
            var start = Sunday.AddDays(week * 7);
            var index = PlanningWeek.IndexOf(start);
            Assert.Equal(start, PlanningWeek.StartOfIndex(index));
            Assert.Equal(DayOfWeek.Sunday, PlanningWeek.StartOfIndex(index).DayOfWeek);
        }
    }

    [Fact]
    public void WeekNumbersDoNotJumpWhenTheDayMoves()
    {
        // 26 October 2026 is day 739,914 of the calendar and the Monday week it opened was number 105,702. The Sunday
        // before it opens the week that now holds it, so the numbers do not jump when the week's first day moves.
        Assert.Equal(739914, new DateOnly(2026, 10, 26).DayNumber);
        Assert.Equal(105702, PlanningWeek.IndexOf(new DateOnly(2026, 10, 26)));
        Assert.Equal(105702, PlanningWeek.IndexOf(Sunday));
        Assert.Equal(105701, PlanningWeek.IndexOf(Sunday.AddDays(-1)));
    }

    [Fact]
    public void AWeekCrossesTheYearAsOne()
    {
        var thursday = new DateOnly(2026, 1, 1);
        var start = new DateOnly(2025, 12, 28);

        Assert.Equal(start, PlanningWeek.StartOf(thursday));
        Assert.Equal(new DateOnly(2026, 1, 3), PlanningWeek.EndOf(start));
        Assert.Equal(PlanningWeek.IndexOf(start), PlanningWeek.IndexOf(new DateOnly(2026, 1, 3)));
        Assert.Equal(PlanningWeek.IndexOf(start) + 1, PlanningWeek.IndexOf(new DateOnly(2026, 1, 4)));
        Assert.Equal(new DateOnly(2026, 1, 3), PlanningWeek.Days(start)[6]);
    }

    [Fact]
    public void TheEarliestWeekCannotBeNamed()
    {
        // The calendar's first day is a Monday, so its Sunday does not exist; week zero is the one that cannot be built.
        Assert.Equal(0, PlanningWeek.IndexOf(DateOnly.MinValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanningWeek.StartOfIndex(0));
        Assert.Equal(new DateOnly(1, 1, 7), PlanningWeek.StartOfIndex(1));
    }

    [Fact]
    public void SaturdayNightIsStillThisWeek()
    {
        Assert.Equal(Sunday, PlanningWeek.StartOf(Edt(10, 31, 23, 59), Zone));
        Assert.Equal(new DateOnly(2026, 11, 1), PlanningWeek.StartOf(Est(11, 1, 0, 30), Zone));
    }

    [Fact]
    public void AnInstantIsReadOnTheHouseholdsClock()
    {
        // 03:30 UTC on Sunday 1 November is 11:30 pm on Saturday at home; 05:30 UTC is 12:30 am on Sunday, after the clocks went back.
        Assert.Equal(Sunday, PlanningWeek.StartOf(new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero), Zone));
        Assert.Equal(new DateOnly(2026, 11, 1), PlanningWeek.StartOf(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), Zone));
        Assert.Equal(
            PlanningWeek.IndexOf(Sunday) + 1,
            PlanningWeek.IndexOf(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), Zone));
    }

    [Fact]
    public void FallBackWeekIs169Hours()
    {
        var start = new DateOnly(2026, 11, 1);

        Assert.Equal(Est(11, 1, 0, 0).AddHours(-1), PlanningWeek.Opens(start, Zone));
        Assert.Equal(TimeSpan.FromHours(169), PlanningWeek.Closes(start, Zone) - PlanningWeek.Opens(start, Zone));
    }

    [Fact]
    public void SpringForwardWeekIs167Hours()
    {
        var start = new DateOnly(2026, 3, 8);

        Assert.Equal(Est(3, 8, 0, 0), PlanningWeek.Opens(start, Zone));
        Assert.Equal(TimeSpan.FromHours(167), PlanningWeek.Closes(start, Zone) - PlanningWeek.Opens(start, Zone));
    }

    [Fact]
    public void AnOrdinaryWeekIs168Hours()
    {
        Assert.Equal(TimeSpan.FromHours(168), PlanningWeek.Closes(Sunday, Zone) - PlanningWeek.Opens(Sunday, Zone));
        Assert.Equal(PlanningWeek.Opens(Sunday.AddDays(7), Zone), PlanningWeek.Closes(Sunday, Zone));
    }

    [Fact]
    public void OpeningAndClosingNeedASunday()
    {
        Assert.Throws<ArgumentException>(() => PlanningWeek.Opens(new DateOnly(2026, 10, 26), Zone));
        Assert.Throws<ArgumentException>(() => PlanningWeek.Closes(new DateOnly(2026, 10, 26), Zone));
    }
}
