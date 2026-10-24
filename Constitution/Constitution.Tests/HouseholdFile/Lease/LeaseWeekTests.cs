using Constitution.Domain.Household.Lease;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.HouseholdFile;

/// <summary>The lease's week is Monday to Monday on the household's clock, daylight-saving weeks included.</summary>
public class LeaseWeekTests
{
    [Fact]
    public void WeeksStartOnMondays()
    {
        for (var index = IndexOfOpening() - 3; index < IndexOfOpening() + 60; index++)
        {
            Assert.Equal(DayOfWeek.Monday, LeaseWeek.MondayOfIndex(index).DayOfWeek);
        }
    }

    [Fact]
    public void ConsecutiveWeeksAreConsecutiveIndexes()
    {
        Assert.Equal(IndexOfOpening() + 1, LeaseWeek.IndexOf(Est(11, 2, 9), Zone));
        Assert.Equal(IndexOfOpening() + 5, LeaseWeek.IndexOf(Est(11, 30, 9), Zone));
    }

    [Fact]
    public void AllOfTheWeekIsOneIndex()
    {
        var index = IndexOfOpening();
        Assert.Equal(index, LeaseWeek.IndexOf(Edt(10, 26, 0, 0), Zone));
        Assert.Equal(index, LeaseWeek.IndexOf(Edt(10, 28, 15), Zone));
        Assert.Equal(index, LeaseWeek.IndexOf(Est(11, 1, 23, 59), Zone));
    }

    [Fact]
    public void HouseholdMondayMidnightOpensTheWeek()
    {
        Assert.NotEqual(LeaseWeek.IndexOf(Est(11, 1, 23, 59), Zone), LeaseWeek.IndexOf(Est(11, 2, 0, 0), Zone));
    }

    [Fact]
    public void TheClocksGoingBackMovesNoBoundary()
    {
        // 03:30 UTC on Monday 2 November is 10:30 pm on Sunday 1 November in the household: still last week.
        var lateSundayAtHome = new DateTimeOffset(2026, 11, 2, 3, 30, 0, TimeSpan.Zero);
        Assert.Equal(IndexOfOpening(), LeaseWeek.IndexOf(lateSundayAtHome, Zone));
        Assert.Equal(new DateOnly(2026, 10, 26), LeaseWeek.MondayOf(lateSundayAtHome, Zone));
        Assert.Equal(IndexOfOpening() + 1, LeaseWeek.IndexOf(new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero), Zone));
    }

    [Fact]
    public void TheClocksGoingForwardMovesNoBoundary()
    {
        // Spring forward is Sunday 8 March 2026 at 2 am; the next Monday opens at midnight daylight time.
        var sundayNight = Edt(3, 8, 23, 59);
        var mondayMorning = Edt(3, 9, 0, 0);
        Assert.Equal(new DateOnly(2026, 3, 2), LeaseWeek.MondayOf(sundayNight, Zone));
        Assert.Equal(new DateOnly(2026, 3, 9), LeaseWeek.MondayOf(mondayMorning, Zone));
        Assert.Equal(LeaseWeek.IndexOf(sundayNight, Zone) + 1, LeaseWeek.IndexOf(mondayMorning, Zone));
    }

    [Fact]
    public void AWeekStartsAtHouseholdMidnight()
    {
        var index = LeaseWeek.IndexOf(Est(11, 4, 12), Zone);
        Assert.Equal(Est(11, 2, 0, 0), LeaseWeek.StartOf(index, Zone));
        Assert.Equal(Edt(3, 9, 0, 0), LeaseWeek.StartOf(LeaseWeek.IndexOf(Edt(3, 12, 9), Zone), Zone));
    }

    [Fact]
    public void FallBackWeekIsAnHourLonger()
    {
        var index = IndexOfOpening();
        Assert.Equal(TimeSpan.FromHours(169), LeaseWeek.StartOf(index + 1, Zone) - LeaseWeek.StartOf(index, Zone));
    }

    [Fact]
    public void SpringForwardWeekIsAnHourShorter()
    {
        var index = LeaseWeek.IndexOf(Edt(3, 4, 9), Zone);
        Assert.Equal(TimeSpan.FromHours(167), LeaseWeek.StartOf(index + 1, Zone) - LeaseWeek.StartOf(index, Zone));
    }

    [Fact]
    public void ItAgreesWithTheWorkoutWeeksMonday()
    {
        // The lease and the weekly workout run must never disagree about which week it is.
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var hour = 0; hour < 24 * 400; hour += 5)
        {
            var at = instant.AddHours(hour);
            Assert.Equal(WorkoutWeekJoin.MondayOf(at, Zone), LeaseWeek.MondayOf(at, Zone));
        }
    }

    private static long IndexOfOpening() => LeaseWeek.IndexOf(Edt(10, 26, 8), Zone);
}
