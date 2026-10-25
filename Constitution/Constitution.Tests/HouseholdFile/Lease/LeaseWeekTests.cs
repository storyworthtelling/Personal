using Constitution.Domain.Calendar;
using Constitution.Domain.Household.Lease;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.HouseholdFile;

/// <summary>The lease's week is Sunday to Sunday on the household's clock, daylight-saving weeks included.</summary>
public class LeaseWeekTests
{
    [Fact]
    public void WeeksStartOnSundays()
    {
        for (var index = IndexOfOpening() - 3; index < IndexOfOpening() + 60; index++)
        {
            Assert.Equal(DayOfWeek.Sunday, LeaseWeek.FirstDayOfIndex(index).DayOfWeek);
        }
    }

    [Fact]
    public void ConsecutiveWeeksAreConsecutiveIndexes()
    {
        Assert.Equal(IndexOfOpening() + 1, LeaseWeek.IndexOf(Est(11, 1, 9), Zone));
        Assert.Equal(IndexOfOpening() + 5, LeaseWeek.IndexOf(Est(11, 30, 9), Zone));
    }

    [Fact]
    public void AllOfTheWeekIsOneIndex()
    {
        var index = IndexOfOpening();
        Assert.Equal(index, LeaseWeek.IndexOf(Edt(10, 25, 0, 0), Zone));
        Assert.Equal(index, LeaseWeek.IndexOf(Edt(10, 28, 15), Zone));
        Assert.Equal(index, LeaseWeek.IndexOf(Edt(10, 31, 23, 59), Zone));
    }

    [Fact]
    public void HouseholdSundayMidnightOpensTheWeek()
    {
        Assert.NotEqual(LeaseWeek.IndexOf(Edt(10, 31, 23, 59), Zone), LeaseWeek.IndexOf(Edt(11, 1, 0, 0), Zone));
    }

    [Fact]
    public void TheClocksGoingBackMovesNoBoundary()
    {
        // 03:30 UTC on Sunday 1 November is 11:30 pm on Saturday 31 October in the household: still last week.
        var lateSaturdayAtHome = new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero);
        Assert.Equal(IndexOfOpening(), LeaseWeek.IndexOf(lateSaturdayAtHome, Zone));
        Assert.Equal(new DateOnly(2026, 10, 25), LeaseWeek.FirstDayOf(lateSaturdayAtHome, Zone));
        Assert.Equal(IndexOfOpening() + 1, LeaseWeek.IndexOf(new DateTimeOffset(2026, 11, 1, 4, 30, 0, TimeSpan.Zero), Zone));
        // The clocks go back at 06:00 UTC that same Sunday and the week is already its own.
        Assert.Equal(IndexOfOpening() + 1, LeaseWeek.IndexOf(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), Zone));
    }

    [Fact]
    public void TheClocksGoingForwardMovesNoBoundary()
    {
        // Spring forward is Sunday 8 March 2026 at 2 am; that Sunday opens its week at midnight standard time, before the jump.
        var saturdayNight = Est(3, 7, 23, 59);
        var sundayMorning = Est(3, 8, 0, 0);
        Assert.Equal(new DateOnly(2026, 3, 1), LeaseWeek.FirstDayOf(saturdayNight, Zone));
        Assert.Equal(new DateOnly(2026, 3, 8), LeaseWeek.FirstDayOf(sundayMorning, Zone));
        Assert.Equal(LeaseWeek.IndexOf(saturdayNight, Zone) + 1, LeaseWeek.IndexOf(sundayMorning, Zone));
    }

    [Fact]
    public void AWeekStartsAtHouseholdMidnight()
    {
        var index = LeaseWeek.IndexOf(Est(11, 4, 12), Zone);
        Assert.Equal(Edt(11, 1, 0, 0), LeaseWeek.StartOf(index, Zone));
        Assert.Equal(Est(3, 8, 0, 0), LeaseWeek.StartOf(LeaseWeek.IndexOf(Edt(3, 12, 9), Zone), Zone));
    }

    [Fact]
    public void FallBackWeekIsAnHourLonger()
    {
        // The week that opens on Sunday 1 November holds the change, which happens at 2 am that morning.
        var index = IndexOfOpening() + 1;
        Assert.Equal(TimeSpan.FromHours(169), LeaseWeek.StartOf(index + 1, Zone) - LeaseWeek.StartOf(index, Zone));
        Assert.Equal(TimeSpan.FromHours(168), LeaseWeek.StartOf(index, Zone) - LeaseWeek.StartOf(index - 1, Zone));
    }

    [Fact]
    public void SpringForwardWeekIsAnHourShorter()
    {
        var index = LeaseWeek.IndexOf(Edt(3, 10, 9), Zone);
        Assert.Equal(TimeSpan.FromHours(167), LeaseWeek.StartOf(index + 1, Zone) - LeaseWeek.StartOf(index, Zone));
    }

    [Fact]
    public void ItAgreesWithThePlanningWeek()
    {
        // The lease and the weekly workout run must never disagree about which week it is: both read PlanningWeek.
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var hour = 0; hour < 24 * 400; hour += 5)
        {
            var at = instant.AddHours(hour);
            Assert.Equal(PlanningWeek.StartOf(at, Zone), LeaseWeek.FirstDayOf(at, Zone));
            Assert.Equal(PlanningWeek.IndexOf(at, Zone), LeaseWeek.IndexOf(at, Zone));
        }
    }

    private static long IndexOfOpening() => LeaseWeek.IndexOf(Edt(10, 26, 8), Zone);
}
