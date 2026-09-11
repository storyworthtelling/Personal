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

    // The invited addresses. An invited address is a FACT about who is going, where a name in a
    // title is a heuristic — which is why D31 reads these first. ResponseStatus is deliberately not
    // carried: a family that never answers an invitation would otherwise get nothing.
    public IReadOnlyList<string> Guests { get; init; } = [];

    // WHICH COLUMNS the event belongs to — a set, because one event can belong to several people
    // (D31). "Gwen and Eva to swimming" is one event in two columns, not two events and not a guess
    // between them. Assigned by CalendarColumnMapper via ColumnSet.Resolve, and never empty: an
    // event in the calendar and on no page is the one outcome worse than being in the wrong column.
    public IReadOnlyList<PersonColumn> Columns { get; set; } = [PersonColumn.Matt];

    // Classification used by the outlook selector and work-block augmentation.
    public EventKind Kind { get; set; } = EventKind.Normal;

    public RecurrenceFrequency Recurrence { get; init; } = RecurrenceFrequency.None;

    // The COUNT from the event's RRULE when present (null when open-ended or non-recurring).
    public int? RecurrenceCount { get; init; }
}
