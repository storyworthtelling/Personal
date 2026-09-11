using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

// Filters a window of upcoming events down to the non-routine ones for the seven-day
// outlook page. See the rules captured in the plan.
public class OutlookSelector : IOutlookSelector
{
    private const int RoutineOccurrenceThreshold = 5;

    public IReadOnlyList<CalendarEvent> SelectNonRoutine(IReadOnlyList<CalendarEvent> upcoming) =>
        upcoming.Where(IsNonRoutine).OrderBy(e => e.Start).ToList();

    private static bool IsNonRoutine(CalendarEvent e)
    {
        // Off days always surface, regardless of column or recurrence.
        if (e.Kind == EventKind.OffDay)
            return true;

        if (e.Kind is EventKind.WorkBlock or EventKind.SchoolBlock)
            return false;

        // Sports and TV (the Watch column) are routine entertainment, not schedule deviations.
        if (e.Columns.Contains(PersonColumn.Watch))
            return false;

        return !IsHighFrequencyRecurring(e);
    }

    // Daily/weekly series with more than five occurrences are background routine; monthly or
    // rarer recurrences, and short series, are kept.
    private static bool IsHighFrequencyRecurring(CalendarEvent e)
    {
        if (e.Recurrence is not (RecurrenceFrequency.Daily or RecurrenceFrequency.Weekly))
            return false;

        return e.RecurrenceCount is null || e.RecurrenceCount > RoutineOccurrenceThreshold;
    }
}
