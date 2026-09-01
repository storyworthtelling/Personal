using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface ICalendarPdfComposer
{
    // Page 1: today's five-column timeline. Page 2: the seven-day outlook of non-routine
    // events. Unreadable carryover notes are listed for re-entry under the handwriting region.
    byte[] Compose(
        DayAgenda today,
        IReadOnlyList<CalendarEvent> outlook,
        IReadOnlyList<ParsedNote> unreadableCarryovers);
}
