using System.Globalization;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using GoogleEvent = global::Google.Apis.Calendar.v3.Data.Event;
using GoogleDateTime = global::Google.Apis.Calendar.v3.Data.EventDateTime;

namespace Constitution.Infrastructure.Calendar;

/// <summary>Why an event on a calendar was not handed over at all (which is not the same as handing it over as free).</summary>
public enum EventLeftOut
{
    /// <summary>The calendar's own owner declined it: they are not going, and the trip detector must not read a declined holiday as one.</summary>
    Declined,

    /// <summary>Google reports it cancelled.</summary>
    Cancelled,

    /// <summary>The app wrote it (it carries the private <c>constitutionKey</c>): read back as busy time, the week would never settle.</summary>
    OurOwn,
}

/// <summary>What one Google event became.</summary>
public abstract record EventMapping
{
    private EventMapping()
    {
    }

    /// <summary>An entry for the busy-time model.</summary>
    public sealed record Kept(CalendarEntry Entry) : EventMapping;

    /// <summary>Deliberately not handed over.</summary>
    public sealed record LeftOut(EventLeftOut Why) : EventMapping;

    /// <summary>
    /// An event that cannot be understood (no start, an end before its start, a date that is not a date). It is not skipped: an entry left out is a
    /// gap in someone's day that nothing downstream can see, so the calendar that holds it is unreadable. <see cref="Reason"/> quotes nothing.
    /// </summary>
    public sealed record Unusable(string Reason) : EventMapping;
}

/// <summary>
/// Google Calendar's event into the busy-time model's entry (<see cref="CalendarEntry"/>), by the rules the calendar seam states
/// (<c>IHouseholdCalendarsSource</c>). Pure: no network, so each rule has a test on a plain <c>Event</c>.
///
/// <list type="bullet">
///   <item>An event with the private <c>constitutionKey</c> property is the app's own and is dropped, whatever else it is. The property is checked
///   among the <i>private</i> properties only, which is where the app writes it.</item>
///   <item>A cancelled event is left out. So is one the calendar's owner declined: the attendee who is the calendar itself (flagged <c>self</c> by
///   Google, which means "the calendar this copy is on", or carrying the calendar's own address) said declined. Never handed over as free.</item>
///   <item>Transparent is free; anything else, tentative and unanswered included, takes the time.</item>
///   <item>An all-day event is a pair of dates with the end exclusive, the way Google writes it; one with no end, or an end that is not after its
///   start, is one day.</item>
/// </list>
/// </summary>
public static class GoogleEventMapper
{
    private const string Cancelled = "cancelled";
    private const string Declined = "declined";
    private const string Transparent = "transparent";
    private const string UnusableReason = "An event on a calendar could not be understood.";

    public static EventMapping Map(GoogleEvent googleEvent, string calendarId)
    {
        ArgumentNullException.ThrowIfNull(googleEvent);

        if (googleEvent.ExtendedProperties?.Private__ is { } properties && properties.ContainsKey(PlacementKey.Property))
        {
            return new EventMapping.LeftOut(EventLeftOut.OurOwn);
        }

        if (string.Equals(googleEvent.Status, Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return new EventMapping.LeftOut(EventLeftOut.Cancelled);
        }

        if (DeclinedByTheCalendarsOwner(googleEvent, calendarId))
        {
            return new EventMapping.LeftOut(EventLeftOut.Declined);
        }

        var availability = string.Equals(googleEvent.Transparency, Transparent, StringComparison.OrdinalIgnoreCase)
            ? Availability.Free
            : Availability.Busy;
        var guests = (googleEvent.Attendees ?? [])
            .Select(attendee => attendee.Email?.Trim())
            .OfType<string>()
            .Where(email => email.Length > 0)
            .ToList();
        var title = googleEvent.Summary ?? string.Empty;

        if (googleEvent.Start?.Date is { } firstText)
        {
            return AllDay(title, firstText, googleEvent.End?.Date, availability, guests);
        }

        if (googleEvent.Start?.DateTimeDateTimeOffset is not { } start)
        {
            return new EventMapping.Unusable(UnusableReason);
        }

        var end = googleEvent.End?.DateTimeDateTimeOffset ?? start;
        return end < start
            ? new EventMapping.Unusable(UnusableReason)
            : new EventMapping.Kept(CalendarEntry.Timed(title, start, end, availability, guests));
    }

    private static EventMapping AllDay(string title, string firstText, string? endText, Availability availability, IReadOnlyList<string> guests)
    {
        if (!TryDate(firstText, out var first))
        {
            return new EventMapping.Unusable(UnusableReason);
        }

        // Google writes the end as the day after the last; one that is missing, or not after the start, is a single day.
        var endExclusive = endText is not null && TryDate(endText, out var end) && end > first ? end : first.AddDays(1);
        return new EventMapping.Kept(CalendarEntry.AllDay(title, first, endExclusive, availability, guests));
    }

    private static bool DeclinedByTheCalendarsOwner(GoogleEvent googleEvent, string calendarId) =>
        (googleEvent.Attendees ?? []).Any(attendee =>
            string.Equals(attendee.ResponseStatus, Declined, StringComparison.OrdinalIgnoreCase)
            && (attendee.Self == true || string.Equals(attendee.Email?.Trim(), calendarId?.Trim(), StringComparison.OrdinalIgnoreCase)));

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
