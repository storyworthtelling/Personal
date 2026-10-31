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

    /// <summary>
    /// The reading run wrote it (it carries the private <c>constitutionKey</c>, and a key of that run's <see cref="KeyFamily"/> when the run names
    /// one): read back as busy time, its week would never settle. An event of the other run's family is somebody's time and is kept.
    /// </summary>
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
///   <item>An event with the private <c>constitutionKey</c> property is the reading run's own and is dropped, whatever else it is. The property is
///   checked among the <i>private</i> properties only, which is where the app writes it. <b>Whose own is told by the key</b> (<see cref="KeyFamilies"/>):
///   a run that names its family (<c>ownFamily</c>) drops only keys of that family, so the workout run sees the meal run's cook blocks, slow-cooker
///   starts and Groceries trip as the busy time they are, and the meal run sees the workouts. A key of no family (unparseable, a kind no run owns
///   yet) is kept by both: unknown means somebody's time. With no family named every keyed event is dropped, as before the families existed.</item>
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

    /// <param name="googleEvent">The event as Google sent it.</param>
    /// <param name="calendarId">The calendar the copy is on, to tell the owner's own answer from a guest's.</param>
    /// <param name="ownFamily">The reading run's family: only keys of this family are its own and dropped. Null drops every keyed event. Never <see cref="KeyFamily.Unknown"/>, which no run owns.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownFamily"/> is <see cref="KeyFamily.Unknown"/>: dropping the unknown would be dropping somebody's time.</exception>
    public static EventMapping Map(GoogleEvent googleEvent, string calendarId, KeyFamily? ownFamily = null)
    {
        ArgumentNullException.ThrowIfNull(googleEvent);
        if (ownFamily == KeyFamily.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(ownFamily), ownFamily, "No run owns the unknown family, and dropping its events would drop somebody's time.");
        }

        if (googleEvent.ExtendedProperties?.Private__ is { } properties
            && properties.TryGetValue(PlacementKey.Property, out var storedKey)
            && (ownFamily is null || KeyFamilies.Of(storedKey) == ownFamily))
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
