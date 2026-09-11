namespace Foreword.Domain.Models;

// The events for a single day, already mapped to columns and augmented with work blocks.
public class DayAgenda
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<CalendarEvent> Events { get; init; }

    // Calendars nobody has mapped (D34). Their events are NOT in Events — they are held — and the
    // page names them, so "where is my thing" is answered on the paper rather than in a log.
    public IReadOnlyList<string> HeldCalendars { get; init; } = [];

    // The drives measured for today, and the house the morning starts from. On the agenda rather
    // than on each event because a drive has two ends and the columns do not share them: the same
    // field is a different drive from the office than it is from the school. A day nobody measured
    // simply draws no bands.
    public Calendar.TravelTable Travel { get; set; } = Calendar.TravelTable.NothingMeasured;
}
