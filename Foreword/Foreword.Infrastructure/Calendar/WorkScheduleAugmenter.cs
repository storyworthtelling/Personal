using System.Globalization;
using Microsoft.Extensions.Options;
using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Calendar;

// Adds synthetic weekday work blocks for each configured worker and marks all-day off
// events, which suppress that worker's block for the day.
public class WorkScheduleAugmenter
{
    private readonly WorkScheduleOptions options;

    public WorkScheduleAugmenter(IOptions<WorkScheduleOptions> options)
    {
        this.options = options.Value;
    }

    public IReadOnlyList<CalendarEvent> Augment(IReadOnlyList<CalendarEvent> events, DateOnly date)
    {
        var result = events.ToList();
        MarkOffDays(result);

        if (IsWeekend(date))
            return result;

        foreach (var shift in options.Shifts)
        {
            // A shift that has been turned off is kept in the settings but not drawn, so turning it
            // back on does not mean typing the hours again.
            if (!shift.Enabled || HasOffDay(result, shift.Person, date))
                continue;

            result.Add(new CalendarEvent
            {
                Title = shift.Title,
                Columns = [shift.Person],
                Kind = EventKind.WorkBlock,
                Start = date.ToDateTime(ParseTime(shift.Start)),
                End = date.ToDateTime(ParseTime(shift.End))
            });
        }

        return result;
    }

    // Tags any all-day event whose title contains an off keyword so the work-block
    // suppression and the outlook can recognise it.
    public void MarkOffDays(IReadOnlyList<CalendarEvent> events)
    {
        foreach (var calendarEvent in events)
            if (calendarEvent.IsAllDay && IsOffTitle(calendarEvent.Title))
                calendarEvent.Kind = EventKind.OffDay;
    }

    private bool HasOffDay(IEnumerable<CalendarEvent> events, PersonColumn person, DateOnly date) =>
        events.Any(e => e.Kind == EventKind.OffDay
            && e.Columns.Contains(person)
            && DateOnly.FromDateTime(e.Start) == date);

    private bool IsOffTitle(string title) =>
        options.OffKeywords.Any(k => !string.IsNullOrEmpty(k)
            && title.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}
