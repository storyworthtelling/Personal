using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// What a calendar entry is, and the entries the head must never hand over. A date-only entry is a
/// <see cref="DateOnly"/> pair (never a DateTime that can slide a day) whose end is exclusive, the
/// way Google writes it; a timed one is a pair of instants.
/// </summary>
public class CalendarEntryTests
{
    [Fact]
    public void ATimedEntryKeepsItsInstants()
    {
        var entry = CalendarEntry.Timed("Dentist", Edt(10, 26, 10), Edt(10, 26, 11), guests: [LeeAddress]);

        Assert.False(entry.IsAllDay);
        Assert.Equal(Edt(10, 26, 10), entry.Start);
        Assert.Equal(Edt(10, 26, 11), entry.End);
        Assert.Equal(Availability.Busy, entry.Availability);
        Assert.Equal([LeeAddress], entry.Guests);
    }

    [Fact]
    public void AnAllDayEntryKeepsItsDates()
    {
        var entry = CalendarEntry.AllDay("Trip", Monday, Monday.AddDays(3), Availability.Free);

        Assert.True(entry.IsAllDay);
        Assert.Equal(Monday, entry.FirstDay);
        Assert.Equal(Monday.AddDays(3), entry.EndDayExclusive);
        Assert.Equal(Availability.Free, entry.Availability);
        Assert.Empty(entry.Guests);
    }

    [Fact]
    public void ABackwardsTimedEntryIsRefused()
    {
        var failure = Assert.Throws<ArgumentException>(() => CalendarEntry.Timed("Oops", Edt(10, 26, 11), Edt(10, 26, 10)));

        Assert.Contains("ends before it starts", failure.Message);
    }

    [Fact]
    public void ABackwardsOrEmptyAllDayEntryIsRefused()
    {
        // Exclusive end: an all-day entry that ends the day it starts covers no day at all.
        Assert.Throws<ArgumentException>(() => CalendarEntry.AllDay("Oops", Monday, Monday));
        Assert.Throws<ArgumentException>(() => CalendarEntry.AllDay("Oops", Monday, Monday.AddDays(-1)));
    }

    [Fact]
    public void ANullTitleIsAnEmptyOne()
    {
        // Calendars hold untitled events; they still take time.
        var entry = CalendarEntry.Timed(null!, Edt(10, 26, 10), Edt(10, 26, 11));

        Assert.Equal(string.Empty, entry.Title);
    }

    [Fact]
    public void AReadOfEntriesIsReadable()
    {
        var read = CalendarRead.Of([OnMonday("Dentist", 10, 11)]);

        Assert.True(read.IsReadable);
        Assert.Null(read.UnreadableBecause);
        Assert.Single(read.Entries);
    }

    [Fact]
    public void AnUnreadableReadSaysWhyAndHasNoEntries()
    {
        var read = CalendarRead.Unreadable("Not shared.");

        Assert.False(read.IsReadable);
        Assert.Equal("Not shared.", read.UnreadableBecause);
        Assert.Empty(read.Entries);
    }

    [Fact]
    public void AnUnreadableReadNeedsAReason()
    {
        Assert.Throws<ArgumentException>(() => CalendarRead.Unreadable(" "));
    }

    [Fact]
    public void ASnapshotAnswersEachCalendar()
    {
        var snapshot = Calendars(sam: [OnMonday("Dentist", 10, 11)], family: [OnMonday("Recital", 15, 16)], children: [OnMonday("Soccer", 16, 17)]);

        Assert.Single(snapshot.OwnCalendar("sam", Edt(10, 26, 0), Edt(10, 27, 0)).Entries);
        Assert.Single(snapshot.FamilyCalendar(Edt(10, 26, 0), Edt(10, 27, 0)).Entries);
        Assert.Single(snapshot.ChildrensCalendars(Edt(10, 26, 0), Edt(10, 27, 0)).Entries);
    }

    [Fact]
    public void ASnapshotMissingAPersonIsUnreadable()
    {
        var snapshot = new HouseholdCalendarSnapshot(
            new Dictionary<string, CalendarRead>(StringComparer.OrdinalIgnoreCase),
            CalendarRead.Of([]),
            CalendarRead.Of([]));

        var read = snapshot.OwnCalendar("Pat", Edt(10, 26, 0), Edt(10, 27, 0));

        Assert.False(read.IsReadable);
    }
}
