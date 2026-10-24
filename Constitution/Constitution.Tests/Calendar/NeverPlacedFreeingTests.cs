using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// What may free a person's work hours (review finding N1). Freeing is the unsafe direction: a day shown free that is not is a
/// workout on top of someone's day. An entry on the family calendar that names nobody is not anyone's day off.
/// </summary>
public class NeverPlacedFreeingTests
{
    private static BusyTime ForSam(HouseholdCalendarSnapshot calendars) => BusyTime.ForDay(Sam, Monday, Rules(), calendars);

    private static BusyTime ForLee(HouseholdCalendarSnapshot calendars) => BusyTime.ForDay(Lee, Monday, Rules(), calendars);

    private static CalendarEntry AllDay(string title) => CalendarEntry.AllDay(title, Monday, Monday.AddDays(1), Availability.Free);

    private static bool AtWork(BusyTime time) => time.NeverPlaced.Any(interval => interval.Reasons.HasFlag(BusyReason.WorkHours));

    [Fact]
    public void KidsOffSchoolOnTheFamilyCalendarLeavesBothAdultsAtWork()
    {
        var calendars = Calendars(family: [AllDay("Kids off school")]);

        Assert.True(AtWork(ForSam(calendars)));
        Assert.True(AtWork(ForLee(calendars)));
    }

    [Theory]
    [InlineData("Vacation")]
    [InlineData("PTO")]
    [InlineData("Day off")]
    public void AnUnnamedOffWordOnTheFamilyCalendarFreesNobody(string title)
    {
        var calendars = Calendars(family: [AllDay(title)]);

        Assert.True(AtWork(ForSam(calendars)));
        Assert.True(AtWork(ForLee(calendars)));
    }

    [Fact]
    public void ANamedOffDayOnTheFamilyCalendarFreesOnlyWhoItNames()
    {
        var calendars = Calendars(family: [AllDay("Lee off")]);

        Assert.True(AtWork(ForSam(calendars)));
        Assert.False(AtWork(ForLee(calendars)));
    }

    [Fact]
    public void AnInvitedAddressOnTheFamilyCalendarNamesWhoIsOff()
    {
        var calendars = Calendars(family: [CalendarEntry.AllDay("Day off", Monday, Monday.AddDays(1), Availability.Free, [LeeAddress])]);

        Assert.True(AtWork(ForSam(calendars)));
        Assert.False(AtWork(ForLee(calendars)));
    }

    [Fact]
    public void AnUnnamedOffDayOnAnOwnCalendarFreesTheOwner()
    {
        var calendars = Calendars(sam: [AllDay("PTO")]);

        Assert.False(AtWork(ForSam(calendars)));
        Assert.True(AtWork(ForLee(calendars)));
    }

    [Fact]
    public void AnOffDayOnAnOwnCalendarNamingSomeoneElseFreesNotTheOwner()
    {
        var calendars = Calendars(sam: [AllDay("Lee off")]);

        Assert.True(AtWork(ForSam(calendars)));
    }
}
