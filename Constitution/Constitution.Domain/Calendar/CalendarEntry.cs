namespace Constitution.Domain.Calendar;

/// <summary>Whether an entry takes its owner's time. Google's "free" (transparent) entries do not.</summary>
public enum Availability
{
    /// <summary>The entry blocks the time it covers. Google's default, and so this one's.</summary>
    Busy,

    /// <summary>The entry is shown on the calendar but does not take anyone's time.</summary>
    Free,
}

/// <summary>
/// One entry on a calendar, as the busy-time model needs to know it: when, what it is called, who
/// is invited, and whether it takes the owner's time. It carries no calendar id, colour or link.
///
/// <para><b>Two shapes, never mixed.</b> A <i>timed</i> entry is a pair of instants
/// (<see cref="DateTimeOffset"/>, so a daylight-saving change cannot move it). An <i>all-day</i>
/// entry is a pair of dates (<see cref="DateOnly"/>, never a DateTime that can slide a day) whose end
/// is <b>exclusive</b>, the way Google writes it: an all-day Monday-to-Wednesday event ends on
/// Thursday. Which local hours an all-day entry covers is the zone's business, so it is decided
/// where the zone is known and not here.</para>
///
/// <para>The head maps the calendar to this. Declined events, cancelled events and anything Google
/// marks transparent are the head's to hand over as <see cref="Availability.Free"/> (or leave out);
/// recurring events arrive already expanded to their occurrences. A tentative or unanswered entry
/// is <see cref="Availability.Busy"/>: when in doubt the time is taken.</para>
/// </summary>
public sealed class CalendarEntry
{
    private CalendarEntry(
        string? title,
        bool isAllDay,
        DateTimeOffset start,
        DateTimeOffset end,
        DateOnly firstDay,
        DateOnly endDayExclusive,
        Availability availability,
        IReadOnlyList<string>? guests)
    {
        Title = title?.Trim() ?? string.Empty;
        IsAllDay = isAllDay;
        Start = start;
        End = end;
        FirstDay = firstDay;
        EndDayExclusive = endDayExclusive;
        Availability = availability;
        Guests = (guests ?? []).Where(guest => !string.IsNullOrWhiteSpace(guest)).Select(guest => guest.Trim()).ToList();
    }

    /// <summary>The title, trimmed; empty for an untitled entry (which still takes time).</summary>
    public string Title { get; }

    /// <summary>True for a date-only entry; false for one with a start and end instant.</summary>
    public bool IsAllDay { get; }

    /// <summary>A timed entry's start. Meaningless on an all-day entry.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>A timed entry's end. May equal <see cref="Start"/>; never earlier. Meaningless on an all-day entry.</summary>
    public DateTimeOffset End { get; }

    /// <summary>An all-day entry's first day. Meaningless on a timed entry.</summary>
    public DateOnly FirstDay { get; }

    /// <summary>The day after an all-day entry's last day. Meaningless on a timed entry.</summary>
    public DateOnly EndDayExclusive { get; }

    public Availability Availability { get; }

    /// <summary>The invited addresses. An invited address is a fact where a name in a title is a heuristic.</summary>
    public IReadOnlyList<string> Guests { get; }

    /// <summary>A timed entry. Ending at the instant it starts is allowed; ending before it is refused.</summary>
    public static CalendarEntry Timed(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        Availability availability = Availability.Busy,
        IReadOnlyList<string>? guests = null)
    {
        if (end < start)
        {
            throw new ArgumentException("A timed calendar entry ends before it starts; the calendar handed over a malformed entry.", nameof(end));
        }

        return new CalendarEntry(title, false, start, end, default, default, availability, guests);
    }

    /// <summary>
    /// An all-day entry from <paramref name="firstDay"/> up to but not including
    /// <paramref name="endDayExclusive"/>, which must be later.
    /// </summary>
    public static CalendarEntry AllDay(
        string title,
        DateOnly firstDay,
        DateOnly endDayExclusive,
        Availability availability = Availability.Busy,
        IReadOnlyList<string>? guests = null)
    {
        if (endDayExclusive <= firstDay)
        {
            throw new ArgumentException(
                "An all-day calendar entry must cover at least one day; its end date is exclusive, so it has to be after its first day.",
                nameof(endDayExclusive));
        }

        return new CalendarEntry(title, true, default, default, firstDay, endDayExclusive, availability, guests);
    }
}
