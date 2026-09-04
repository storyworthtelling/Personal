using Android.Content;
using Android.Database;
using Android.Provider;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The calendar, read for clashes and written when the owner says so.
///
/// <b>Provider to read, API to write</b> was the decision, and the read half is what makes
/// a proposal worth making: a date offered without checking the day is a date the owner has
/// to go and check themselves, which is the work the detection was supposed to save.
///
/// The write is the provider too, not the Google API. It needs no OAuth scope, no network
/// and no second sign-in, and Google's own sync carries the event up within the minute —
/// so for "put this in my calendar" it is the same outcome for a fraction of the machinery.
/// If Foreword (P9) takes the job over, it takes it over from a single call site.
///
/// Everything here is best-effort. No calendar permission, no calendar, a provider that
/// refuses: the message still arrives and the date is still detected. A missing event is
/// worse than no event only if the owner was told there would be one.
/// </summary>
internal static class CalendarAccess
{
    /// <summary>Everything already in that day, for the clash check.</summary>
    public static IReadOnlyList<Appointment> Day(Context context, DateOnly day)
    {
        var appointments = new List<Appointment>();

        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = day.ToDateTime(TimeOnly.MaxValue);

        using var cursor = context.ContentResolver?.Query(
            CalendarContract.Events.ContentUri!,
            [CalendarContract.Events.InterfaceConsts.Title,
             CalendarContract.Events.InterfaceConsts.Dtstart,
             CalendarContract.Events.InterfaceConsts.Dtend],
            $"{CalendarContract.Events.InterfaceConsts.Dtstart} >= ? AND " +
            $"{CalendarContract.Events.InterfaceConsts.Dtstart} <= ? AND " +
            $"{CalendarContract.Events.InterfaceConsts.Deleted} = 0",
            [Millis(start).ToString(), Millis(end).ToString()],
            null);

        while (cursor is not null && cursor.MoveToNext())
        {
            var title = cursor.IsNull(0) ? "(untitled)" : cursor.GetString(0)!;
            var startsMillis = cursor.GetLong(1);
            var endsMillis = cursor.IsNull(2) ? startsMillis : cursor.GetLong(2);

            appointments.Add(new Appointment(
                title,
                DateTimeOffset.FromUnixTimeMilliseconds(startsMillis).LocalDateTime,
                TimeSpan.FromMilliseconds(Math.Max(0, endsMillis - startsMillis))));
        }

        return appointments;
    }

    /// <summary>
    /// Writes the event, and returns whether it landed. The owner confirmed it — the
    /// proposal is never written silently — so a failure has to be reported rather than
    /// swallowed: believing something is in the calendar when it is not is the one outcome
    /// worse than not offering at all.
    /// </summary>
    public static bool Write(Context context, ProposedEvent proposal)
    {
        var calendarId = PrimaryCalendarId(context);
        if (calendarId is null)
        {
            return false;
        }

        var starts = proposal.StartsOn.ToDateTime(proposal.StartsAt ?? TimeOnly.MinValue);
        var allDay = proposal.StartsAt is null;

        var values = new ContentValues();
        values.Put(CalendarContract.Events.InterfaceConsts.CalendarId, calendarId.Value);
        values.Put(CalendarContract.Events.InterfaceConsts.Title, proposal.Title);
        values.Put(CalendarContract.Events.InterfaceConsts.Dtstart, Millis(starts));
        values.Put(CalendarContract.Events.InterfaceConsts.Dtend,
            Millis(starts + (allDay ? TimeSpan.FromDays(1) : CalendarConflicts.AssumedDuration)));
        values.Put(CalendarContract.Events.InterfaceConsts.AllDay, allDay ? 1 : 0);
        values.Put(CalendarContract.Events.InterfaceConsts.EventTimezone, TimeZoneInfo.Local.Id);
        values.Put(CalendarContract.Events.InterfaceConsts.Description, "Added from Post.");

        return context.ContentResolver?.Insert(CalendarContract.Events.ContentUri!, values) is not null;
    }

    // The first visible calendar the owner can write to. Choosing between a personal, a
    // family and a daughter's calendar is the owner's call and belongs on the proposal —
    // this picks a default so the common case is one tap rather than a settings trip.
    private static long? PrimaryCalendarId(Context context)
    {
        using var cursor = context.ContentResolver?.Query(
            CalendarContract.Calendars.ContentUri!,
            [CalendarContract.Calendars.InterfaceConsts.Id],
            $"{CalendarContract.Calendars.InterfaceConsts.Visible} = 1 AND " +
            $"{CalendarContract.Calendars.InterfaceConsts.CalendarAccessLevel} >= ?",
            [((int)CalendarAccess2.Owner).ToString()],
            null);

        return cursor is not null && cursor.MoveToFirst() ? cursor.GetLong(0) : null;
    }

    private static long Millis(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    // The access-level constant, named to avoid colliding with this class.
    private enum CalendarAccess2
    {
        Owner = 700,
    }
}
