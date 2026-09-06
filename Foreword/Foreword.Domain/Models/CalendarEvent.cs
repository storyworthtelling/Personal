namespace Foreword.Domain.Models;

// How often an event repeats. Drawn from the master event's RRULE FREQ.
public enum RecurrenceFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

// Classifies an event so the calendar and seven-day outlook can treat it differently.
public enum EventKind
{
    Normal,
    WorkBlock,
    SchoolBlock,
    OffDay
}

// A single calendar entry assigned to one of the five columns.
public class CalendarEvent
{
    public required string Title { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool IsAllDay { get; init; }
    public string? Location { get; init; }

    // The originating Google calendar id; used by the column mapper and for routing.
    public string SourceCalendarId { get; init; } = string.Empty;

    // Which column the event belongs to. Assigned by CalendarColumnMapper.
    public PersonColumn Column { get; set; }

    // Classification used by the outlook selector and work-block augmentation.
    public EventKind Kind { get; set; } = EventKind.Normal;

    public RecurrenceFrequency Recurrence { get; init; } = RecurrenceFrequency.None;

    // The COUNT from the event's RRULE when present (null when open-ended or non-recurring).
    public int? RecurrenceCount { get; init; }
}
