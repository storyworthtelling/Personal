using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IGoogleCalendarClient
{
    // All events across every calendar in the account's calendar list that overlap the
    // given local day. Column is not yet assigned; the caller maps it.
    Task<IReadOnlyList<CalendarEvent>> GetEventsForDay(DateOnly date);

    // All events across every calendar overlapping the inclusive date range, used to build
    // the seven-day outlook.
    Task<IReadOnlyList<CalendarEvent>> GetEventsInRange(DateOnly start, DateOnly end);

    // Creates an event on the calendar mapped to the given column.
    Task AddEvent(CalendarEvent calendarEvent);

    // Ensures a calendar with the given summary exists, returning its id. Used to
    // create a calendar for each column whose person has no account of their own.
    Task<string> EnsureCalendar(string summary);

    // The id/summary of every calendar in the account's calendar list.
    Task<IReadOnlyDictionary<string, string>> ListCalendars();
}
