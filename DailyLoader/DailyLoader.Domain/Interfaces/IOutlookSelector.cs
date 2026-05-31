using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface IOutlookSelector
{
    // Filters a window of upcoming events down to the non-routine ones worth surfacing on
    // the seven-day outlook page (drops work/school blocks, the Watch column, and
    // high-frequency recurring events; keeps off-days and one-off items).
    IReadOnlyList<CalendarEvent> SelectNonRoutine(IReadOnlyList<CalendarEvent> upcoming);
}
