using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// Travel is drawn <b>and</b> flagged. Drawing the drive makes the day's real occupancy visible; the
/// separate flag covers the case where two places cannot both be reached.
/// </summary>
public class TravelPlanTests
{
    private static readonly DateOnly Day = new(2026, 9, 8);

    private static CalendarEvent At(int startHour, int startMinute, int endHour, int endMinute,
        string title, string? location = null) => new()
    {
        Title = title,
        Location = location,
        Columns = [PersonColumn.Ashley],
        Start = Day.ToDateTime(new TimeOnly(startHour, startMinute)),
        End = Day.ToDateTime(new TimeOnly(endHour, endMinute)),
    };

    private static Func<CalendarEvent, int?> Drive(int minutes) => _ => minutes;

    [Fact]
    public void AnEventWithADriveGetsABandBeforeIt()
    {
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([soccer], Drive(25)).Single();

        Assert.Equal(25, travel.Minutes);
        Assert.Equal(Day.ToDateTime(new TimeOnly(15, 35)), travel.Leave);
    }

    /// <summary>
    /// An unknown drive draws nothing. A band of invented length is worse than no band: it would
    /// make the day look occupied by something nobody measured.
    /// </summary>
    [Fact]
    public void AnUnknownDriveDrawsNothing()
    {
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        Assert.Empty(TravelPlan.For([soccer], _ => null));
    }

    [Fact]
    public void AZeroMinuteDriveDrawsNothing()
    {
        Assert.Empty(TravelPlan.For([At(16, 0, 17, 0, "Here", "Home")], Drive(0)));
    }

    /// <summary>
    /// The flag. A three-thirty that runs to four cannot also be a four o'clock twenty-five minutes
    /// away — and the grid alone shows that as two tidy boxes with nothing wrong.
    /// </summary>
    [Fact]
    public void AnEventYouCouldNotReachInTimeIsFlagged()
    {
        var vet = At(15, 30, 16, 0, "Vet");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([vet, soccer], e => e == soccer ? 25 : null).Single();

        Assert.True(travel.Unreachable);
    }

    [Fact]
    public void AnEventWithRoomForTheDriveIsNotFlagged()
    {
        var vet = At(14, 0, 15, 0, "Vet");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([vet, soccer], e => e == soccer ? 25 : null).Single();

        Assert.False(travel.Unreachable);
    }

    /// <summary>
    /// Leaving exactly as the previous event ends is not a clash. It is tight, and the band shows
    /// that it is tight — flagging it as impossible would cry wolf on an ordinary day.
    /// </summary>
    [Fact]
    public void LeavingExactlyAsTheLastThingEndsIsTightRatherThanImpossible()
    {
        var vet = At(15, 0, 15, 35, "Vet");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([vet, soccer], e => e == soccer ? 25 : null).Single();

        Assert.False(travel.Unreachable);
    }

    /// <summary>
    /// The previous event by START time is not always the one that runs latest. A long block that
    /// began this morning is still the thing you cannot leave.
    /// </summary>
    [Fact]
    public void ALongEarlierEventStillCountsAsTheThingYouCannotLeave()
    {
        var allMorning = At(9, 0, 16, 30, "Conference");
        var short1 = At(10, 0, 10, 30, "Call");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([allMorning, short1, soccer], e => e == soccer ? 25 : null)
            .Single();

        Assert.True(travel.Unreachable);
    }

    /// <summary>An all-day event is not somewhere you drive from.</summary>
    [Fact]
    public void AnAllDayEventIsNotSomewhereYouDriveFrom()
    {
        var holiday = new CalendarEvent
        {
            Title = "Holiday",
            IsAllDay = true,
            Columns = [PersonColumn.Ashley],
            Start = Day.ToDateTime(TimeOnly.MinValue),
            End = Day.ToDateTime(TimeOnly.MaxValue),
        };
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([holiday, soccer], e => e == soccer ? 25 : null).Single();

        Assert.False(travel.Unreachable);
    }
}
