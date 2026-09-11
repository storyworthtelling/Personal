using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// Travel is drawn <b>and</b> flagged. Drawing the drive makes the day's real occupancy visible; the
/// separate flag covers the case where two places cannot both be reached.
///
/// <para>A drive has two ends, and the second one is the one that was missing. The event carries
/// where it is; where the person is <i>coming from</i> is not on the event at all — it is wherever
/// that person's day has left them, which is the previous thing in <b>their</b> column, and the
/// house at the start of the morning. A single per-event drive time would be the same number in
/// every column, so a parent leaving work and a child leaving school would be given the same
/// twenty-five minutes to the same field.</para>
/// </summary>
public class TravelPlanTests
{
    private static readonly DateOnly Day = new(2026, 9, 8);
    private const string Home = "Home";

    private static CalendarEvent At(int startHour, int startMinute, int endHour, int endMinute,
        string title, string? location = null) => new()
    {
        Title = title,
        Location = location,
        Columns = [PersonColumn.Jordan],
        Start = Day.ToDateTime(new TimeOnly(startHour, startMinute)),
        End = Day.ToDateTime(new TimeOnly(endHour, endMinute)),
    };

    // Every route takes the same time, when all the test cares about is that there is one.
    private static Func<string, string, int?> Drive(int minutes) => (_, _) => minutes;

    // A route table keyed by both ends, for the tests that care where the drive started.
    private static Func<string, string, int?> Routes(params (string From, string To, int Minutes)[] legs) =>
        (from, to) => legs.FirstOrDefault(l => l.From == from && l.To == to) is { Minutes: > 0 } hit
            ? hit.Minutes
            : null;

    [Fact]
    public void AnEventWithADriveGetsABandBeforeIt()
    {
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([soccer], Home, Drive(25)).Single();

        Assert.Equal(25, travel.Minutes);
        Assert.Equal(Day.ToDateTime(new TimeOnly(15, 35)), travel.Leave);
    }

    /// <summary>
    /// The day starts at the house. Nothing earlier has said where anyone is, and the alternative —
    /// no band at all until someone has been somewhere — would leave the first drive of every
    /// morning, which is usually the school run, as the one drive the page never shows.
    /// </summary>
    [Fact]
    public void TheFirstDriveOfTheDayStartsAtHome()
    {
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([soccer], Home, Drive(25)).Single();

        Assert.Equal(Home, travel.From);
    }

    /// <summary>
    /// The second drive starts where the first one finished. This is what makes the band mean
    /// anything: the drive from the office to the field and the drive from the house to the field
    /// are different drives, and a person who is at the office is not at the house.
    /// </summary>
    [Fact]
    public void ADriveStartsWhereThePersonsDayHasLeftThem()
    {
        var work = At(9, 0, 15, 0, "Shift", "The office");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travels = TravelPlan.For([work, soccer], Home,
            Routes((Home, "The office", 15), ("The office", "Veterans Park", 40)));

        Assert.Equal(Home, travels[0].From);
        Assert.Equal(15, travels[0].Minutes);

        Assert.Equal("The office", travels[1].From);
        Assert.Equal(40, travels[1].Minutes);
    }

    /// <summary>
    /// Somewhere with no address is not somewhere you can be driven to, so it draws nothing — and
    /// it does not move anyone either. A person who was last known to be at the office and then has
    /// an hour of "Call" is still driving from the office afterwards; sending them home for it
    /// would invent a round trip nobody made.
    /// </summary>
    [Fact]
    public void AnEventWithNoPlaceDrawsNothingAndMovesNobody()
    {
        var work = At(9, 0, 12, 0, "Shift", "The office");
        var call = At(13, 0, 13, 30, "Call");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travels = TravelPlan.For([work, call, soccer], Home,
            Routes((Home, "The office", 15), ("The office", "Veterans Park", 40)));

        Assert.Equal(2, travels.Count);
        Assert.DoesNotContain(travels, t => t.Event == call);
        Assert.Equal("The office", travels.Single(t => t.Event == soccer).From);
    }

    /// <summary>Being already there is not a drive.</summary>
    [Fact]
    public void TwoThingsInOnePlaceHaveNoDriveBetweenThem()
    {
        var first = At(9, 0, 10, 0, "Assembly", "The school");
        var second = At(10, 30, 11, 0, "Reading", "The school");

        var travels = TravelPlan.For([first, second], Home, Drive(25));

        Assert.Single(travels);
        Assert.Equal(first, travels[0].Event);
    }

    /// <summary>
    /// One event, two columns, two different drives — the owner's answer to whose column gets the
    /// band: <b>both</b>, the child's and the driver's.
    ///
    /// <para>A parent taking a child to the field is one event on two people, and it is not the
    /// same journey for them. The child comes out of school; the parent comes in from work. Drawing
    /// it in only the child's column would hide the hole it makes in the parent's afternoon, which
    /// is the half of the day that has to be arranged — and drawing one band in both columns from a
    /// single number would put the child's twelve minutes on a parent who is forty away.</para>
    ///
    /// <para>Which is what makes this per column rather than per event: the page calls this once
    /// for each column, over the events in it, and an event in two columns is in both calls.</para>
    /// </summary>
    [Fact]
    public void ADriveTheChildAndTheDriverBothMakeIsInBothColumns()
    {
        var school = At(8, 30, 15, 0, "School", "The school");
        var work = At(9, 0, 15, 30, "Shift", "The office");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");
        soccer.Columns = [PersonColumn.Wren, PersonColumn.Rowan];

        var routes = Routes(
            ("The school", "Veterans Park", 12),
            ("The office", "Veterans Park", 40));

        // The child's column, and then the driver's — exactly as the page walks them.
        var childs = TravelPlan.For([school, soccer], Home, routes).Single(t => t.Event == soccer);
        var drivers = TravelPlan.For([work, soccer], Home, routes).Single(t => t.Event == soccer);

        Assert.Equal("The school", childs.From);
        Assert.Equal(12, childs.Minutes);

        Assert.Equal("The office", drivers.From);
        Assert.Equal(40, drivers.Minutes);

        // And the same event is comfortable for one of them and a clash for the other, which is
        // the whole reason both bands are drawn: the parent cannot leave work at twenty past three.
        Assert.False(childs.Unreachable);
        Assert.True(drivers.Unreachable);
    }

    /// <summary>
    /// An unknown drive draws nothing. A band of invented length is worse than no band: it would
    /// make the day look occupied by something nobody measured.
    /// </summary>
    [Fact]
    public void AnUnknownDriveDrawsNothing()
    {
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        Assert.Empty(TravelPlan.For([soccer], Home, (_, _) => null));
    }

    [Fact]
    public void AZeroMinuteDriveDrawsNothing()
    {
        Assert.Empty(TravelPlan.For([At(16, 0, 17, 0, "Here", "Next door")], Home, Drive(0)));
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

        var travel = TravelPlan.For([vet, soccer], Home, Drive(25)).Single();

        Assert.True(travel.Unreachable);
    }

    [Fact]
    public void AnEventWithRoomForTheDriveIsNotFlagged()
    {
        var vet = At(14, 0, 15, 0, "Vet");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([vet, soccer], Home, Drive(25)).Single();

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

        var travel = TravelPlan.For([vet, soccer], Home, Drive(25)).Single();

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
        var shortCall = At(10, 0, 10, 30, "Call");
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([allMorning, shortCall, soccer], Home, Drive(25)).Single();

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
            Location = "Anywhere",
            Columns = [PersonColumn.Jordan],
            Start = Day.ToDateTime(TimeOnly.MinValue),
            End = Day.ToDateTime(TimeOnly.MaxValue),
        };
        var soccer = At(16, 0, 17, 0, "Soccer practice", "Veterans Park");

        var travel = TravelPlan.For([holiday, soccer], Home, Drive(25)).Single();

        Assert.False(travel.Unreachable);
        Assert.Equal(Home, travel.From);
    }
}
