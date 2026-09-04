using Microsoft.Extensions.Options;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Calendar;

// Assigns each calendar event to one of the five columns using the configured mapping,
// with Family-calendar events routed to Wren/Linden by title keyword.
public class CalendarColumnMapper
{
    private readonly CalendarMappingOptions options;

    public CalendarColumnMapper(IOptions<CalendarMappingOptions> options)
    {
        this.options = options.Value;
    }

    public IReadOnlyList<CalendarEvent> Assign(IEnumerable<CalendarEvent> events)
    {
        var list = events.ToList();
        foreach (var calendarEvent in list)
            calendarEvent.Column = Resolve(calendarEvent);
        return list;
    }

    public PersonColumn Resolve(CalendarEvent calendarEvent)
    {
        var calendarId = calendarEvent.SourceCalendarId;

        if (!string.IsNullOrEmpty(options.FamilyCalendarId) && calendarId == options.FamilyCalendarId)
            return ResolveFamily(calendarEvent.Title);

        if (options.CalendarColumns.TryGetValue(calendarId, out var column))
            return column;

        return options.DefaultColumn;
    }

    private PersonColumn ResolveFamily(string title)
    {
        if (ContainsAny(title, options.GwenKeywords))
            return PersonColumn.Wren;
        if (ContainsAny(title, options.EvaKeywords))
            return PersonColumn.Linden;
        return options.DefaultColumn;
    }

    private static bool ContainsAny(string title, IEnumerable<string> keywords) =>
        keywords.Any(k => !string.IsNullOrEmpty(k)
            && title.Contains(k, StringComparison.OrdinalIgnoreCase));
}
