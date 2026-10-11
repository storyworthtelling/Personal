namespace Pulp.Domain.Calendar;

/// <summary>
/// The household's Pulp calendar as the writer needs it. One implementation talks to Google
/// (<c>GoogleCalendarClient</c>); every method that changes an event does so without notifying anyone.
/// </summary>
public interface IPulpCalendar
{
    Task<string> CreateAsync(string title, TimeZoneInfo zone, CancellationToken cancellationToken);

    /// <summary>The accounts the calendar is already shared with.</summary>
    Task<IReadOnlyList<string>> SharedWithAsync(string calendarId, CancellationToken cancellationToken);

    Task ShareAsReaderAsync(string calendarId, string account, CancellationToken cancellationToken);

    /// <summary>Every event in the range, Pulp's and hand-made alike; hand-made ones come back with no key.</summary>
    Task<IReadOnlyList<ExistingEvent>> ListAsync(string calendarId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken);

    Task InsertAsync(string calendarId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken);

    Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken);

    Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken);
}
