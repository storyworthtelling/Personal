namespace DailyLoader.Domain.Models;

// The events for a single day, already mapped to columns and augmented with work blocks.
public class DayAgenda
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<CalendarEvent> Events { get; init; }
}
