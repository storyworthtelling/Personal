using Constitution.Domain.Calendar;
using Constitution.Infrastructure.Calendar;
using Google.Apis.Calendar.v3.Data;

namespace Constitution.Tests.Calendar.Google;

/// <summary>
/// One Google event into the busy-time model's entry, by the two rules the calendar seam states (<c>IHouseholdCalendarsSource</c>): the
/// app's own events (they carry the private <c>constitutionKey</c>) are dropped, or the planner would plan around its own earlier workouts and the
/// week would never settle; and a declined or cancelled event is left out entirely, never handed over as free, because a trip detector reads a
/// "free" holiday as a holiday. An event Google marks transparent is free; a tentative or unanswered one takes the time.
/// </summary>
public class GoogleEventMapperTests
{
    private const string CalendarId = "sam@example.com";

    private static Event Timed(string title = "Dentist", string? transparency = null) => new()
    {
        Id = "e1",
        Summary = title,
        Status = "confirmed",
        Transparency = transparency,
        Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(-5)) },
        End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.FromHours(-5)) },
    };

    private static Event AllDay(string first, string? last) => new()
    {
        Id = "e2",
        Summary = "Holiday",
        Status = "confirmed",
        Start = new EventDateTime { Date = first },
        End = last is null ? null : new EventDateTime { Date = last },
    };

    private static CalendarEntry Kept(Event googleEvent, string calendarId = CalendarId) =>
        Assert.IsType<EventMapping.Kept>(GoogleEventMapper.Map(googleEvent, calendarId)).Entry;

    private static EventLeftOut LeftOut(Event googleEvent, string calendarId = CalendarId) =>
        Assert.IsType<EventMapping.LeftOut>(GoogleEventMapper.Map(googleEvent, calendarId)).Why;

    private static EventAttendee Attendee(string email, string? response, bool self = false) =>
        new() { Email = email, ResponseStatus = response, Self = self };

    // ---- the shape of an entry ----

    [Fact]
    public void ATimedEventKeepsItsInstantsAndTitle()
    {
        var entry = Kept(Timed());

        Assert.False(entry.IsAllDay);
        Assert.Equal("Dentist", entry.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero), entry.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero), entry.End);
        Assert.Equal(Availability.Busy, entry.Availability);
    }

    [Fact]
    public void AnUntitledEventStillTakesTime()
    {
        var entry = Kept(Timed(title: null!));

        Assert.Equal(string.Empty, entry.Title);
        Assert.Equal(Availability.Busy, entry.Availability);
    }

    [Fact]
    public void AnAllDayEventKeepsItsDatesWithTheEndExclusive()
    {
        var entry = Kept(AllDay("2026-11-09", "2026-11-12"));

        Assert.True(entry.IsAllDay);
        Assert.Equal(new DateOnly(2026, 11, 9), entry.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 12), entry.EndDayExclusive);
    }

    [Fact]
    public void AnAllDayEventWithNoEndIsOneDay()
    {
        var entry = Kept(AllDay("2026-11-09", null));

        Assert.Equal(new DateOnly(2026, 11, 10), entry.EndDayExclusive);
    }

    [Theory]
    [InlineData("2026-11-09")]
    [InlineData("2026-11-01")]
    public void AnAllDayEventThatEndsBeforeItStartsIsOneDay(string end)
    {
        var entry = Kept(AllDay("2026-11-09", end));

        Assert.Equal(new DateOnly(2026, 11, 9), entry.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 10), entry.EndDayExclusive);
    }

    [Fact]
    public void AnEventThatEndsWhereItStartsIsKept()
    {
        var point = Timed();
        point.End = point.Start;

        var entry = Kept(point);

        Assert.Equal(entry.Start, entry.End);
    }

    [Fact]
    public void TheInvitedAddressesComeAcrossAsGuests()
    {
        var invited = Timed();
        invited.Attendees = [Attendee(" lee@example.com ", "accepted"), Attendee("", "accepted"), Attendee("sam@example.com", "needsAction", self: true)];

        Assert.Equal(["lee@example.com", "sam@example.com"], Kept(invited).Guests);
    }

    // ---- availability ----

    [Fact]
    public void ATransparentEventIsFree()
    {
        Assert.Equal(Availability.Free, Kept(Timed(transparency: "transparent")).Availability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("opaque")]
    public void AnythingElseTakesTheTime(string? transparency)
    {
        Assert.Equal(Availability.Busy, Kept(Timed(transparency: transparency)).Availability);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("tentative")]
    [InlineData("needsAction")]
    [InlineData(null)]
    public void AnAnsweredOrUnansweredInvitationTakesTheTime(string? response)
    {
        var invited = Timed();
        invited.Attendees = [Attendee(CalendarId, response, self: true)];

        Assert.Equal(Availability.Busy, Kept(invited).Availability);
    }

    // ---- left out ----

    [Fact]
    public void ADeclinedEventIsLeftOutNotFree()
    {
        var declined = Timed();
        declined.Attendees = [Attendee("lee@example.com", "accepted"), Attendee(CalendarId, "declined", self: true)];

        Assert.Equal(EventLeftOut.Declined, LeftOut(declined));
    }

    [Fact]
    public void ADeclinedAllDayHolidayIsLeftOutToo()
    {
        var holiday = AllDay("2026-11-09", "2026-11-12");
        holiday.Transparency = "transparent";
        holiday.Attendees = [Attendee(CalendarId, "declined", self: true)];

        Assert.Equal(EventLeftOut.Declined, LeftOut(holiday));
    }

    [Fact]
    public void TheCalendarsOwnAnswerCountsEvenWhenGoogleDoesNotFlagItSelf()
    {
        // A calendar read through someone else's account may not carry the self flag; the calendar's own address says whose answer it is.
        var declined = Timed();
        declined.Attendees = [Attendee("SAM@example.com", "declined", self: false)];

        Assert.Equal(EventLeftOut.Declined, LeftOut(declined));
    }

    [Fact]
    public void SomeoneElsesDeclineDoesNotRemoveTheEvent()
    {
        var invited = Timed();
        invited.Attendees = [Attendee("lee@example.com", "declined"), Attendee(CalendarId, "accepted", self: true)];

        Assert.Equal(Availability.Busy, Kept(invited).Availability);
    }

    [Fact]
    public void ACancelledEventIsLeftOut()
    {
        var cancelled = Timed();
        cancelled.Status = "cancelled";

        Assert.Equal(EventLeftOut.Cancelled, LeftOut(cancelled));
    }

    [Fact]
    public void ACancelledEventWithNoTimesIsLeftOutNotUnusable()
    {
        // Google sends a cancelled event as a bare stub.
        Assert.Equal(EventLeftOut.Cancelled, LeftOut(new Event { Id = "gone", Status = "cancelled" }));
    }

    [Fact]
    public void AnEventWeWroteIsLeftOut()
    {
        var ours = Timed("Workout · 40 min");
        ours.ExtendedProperties = new Event.ExtendedPropertiesData { Private__ = new Dictionary<string, string> { ["constitutionKey"] = "c1:sam:strength:2951:0" } };

        Assert.Equal(EventLeftOut.OurOwn, LeftOut(ours));
    }

    [Fact]
    public void OurKeyOnADeclinedOrCancelledEventIsStillOurs()
    {
        var ours = Timed();
        ours.Status = "cancelled";
        ours.ExtendedProperties = new Event.ExtendedPropertiesData { Private__ = new Dictionary<string, string> { ["constitutionKey"] = "x" } };

        Assert.Equal(EventLeftOut.OurOwn, LeftOut(ours));
    }

    [Fact]
    public void ASharedPropertyOfTheSameNameIsNotOurs()
    {
        var other = Timed();
        other.ExtendedProperties = new Event.ExtendedPropertiesData { Shared = new Dictionary<string, string> { ["constitutionKey"] = "x" } };

        Assert.Equal("Dentist", Kept(other).Title);
    }

    [Fact]
    public void AnotherPrivatePropertyIsNotOurs()
    {
        var other = Timed();
        other.ExtendedProperties = new Event.ExtendedPropertiesData { Private__ = new Dictionary<string, string> { ["somethingElse"] = "x" } };

        Assert.Equal("Dentist", Kept(other).Title);
    }

    // ---- unusable ----

    [Fact]
    public void AnEventWithNoStartIsUnusableNotSkipped()
    {
        var nothing = Assert.IsType<EventMapping.Unusable>(GoogleEventMapper.Map(new Event { Id = "x", Status = "confirmed", Summary = "?" }, CalendarId));

        Assert.Equal("An event on a calendar could not be understood.", nothing.Reason);
    }

    [Fact]
    public void ATimedEventThatEndsBeforeItStartsIsUnusable()
    {
        var backwards = Timed();
        backwards.End = new EventDateTime { DateTimeDateTimeOffset = backwards.Start.DateTimeDateTimeOffset!.Value.AddHours(-2) };

        Assert.IsType<EventMapping.Unusable>(GoogleEventMapper.Map(backwards, CalendarId));
    }

    [Fact]
    public void AnAllDayDateThatIsNotADateIsUnusable()
    {
        Assert.IsType<EventMapping.Unusable>(GoogleEventMapper.Map(AllDay("next Tuesday", null), CalendarId));
    }

    [Fact]
    public void AnUnusableReasonQuotesNothing()
    {
        var reason = Assert.IsType<EventMapping.Unusable>(GoogleEventMapper.Map(AllDay("Dentist with Lee", null), CalendarId)).Reason;

        Assert.DoesNotContain("Dentist", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Lee", reason, StringComparison.Ordinal);
    }
}
