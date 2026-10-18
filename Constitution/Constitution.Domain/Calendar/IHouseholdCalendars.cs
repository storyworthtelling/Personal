namespace Constitution.Domain.Calendar;

/// <summary>Which of the household's calendars a read came from.</summary>
public enum CalendarSource
{
    /// <summary>One adult's own calendar.</summary>
    Own,

    /// <summary>The family calendar.</summary>
    Family,

    /// <summary>The children's calendars, taken together.</summary>
    Children,
}

/// <summary>
/// What reading one calendar gave: its entries, or the reason it could not be read. The two are
/// never confused. A calendar nobody could read (the partner has not shared hers yet, Google would
/// not answer) is <b>not</b> a calendar with nothing on it, and the busy-time model treats it as
/// the first, because the second would put a workout on top of an event nobody could see.
/// </summary>
public sealed class CalendarRead
{
    private CalendarRead(IReadOnlyList<CalendarEntry> entries, string? unreadableBecause)
    {
        Entries = entries;
        UnreadableBecause = unreadableBecause;
    }

    /// <summary>The entries; empty when the calendar is unreadable.</summary>
    public IReadOnlyList<CalendarEntry> Entries { get; }

    /// <summary>Why the calendar could not be read, or null when it could.</summary>
    public string? UnreadableBecause { get; }

    public bool IsReadable => UnreadableBecause is null;

    /// <summary>A calendar that was read. It may hold nothing; that is then a free calendar.</summary>
    public static CalendarRead Of(IEnumerable<CalendarEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new CalendarRead(entries.ToList(), null);
    }

    /// <summary>A calendar that could not be read. The reason is a plain sentence with no calendar content in it.</summary>
    public static CalendarRead Unreadable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unreadable calendar has to say why it could not be read.", nameof(reason));
        }

        return new CalendarRead([], reason.Trim());
    }
}

/// <summary>
/// The seam the Android head fills from Google Calendar. The model reads it <b>synchronously</b>:
/// the head fetches the window (network, async) into a <see cref="HouseholdCalendarSnapshot"/>
/// first, and the pure code below never waits on anything.
///
/// <para>Each method returns the entries that overlap <c>[from, to)</c>. It may return more than
/// that (the model drops what falls outside), but never fewer: an entry left out here is a gap in
/// someone's day that nothing downstream can see. Whoever cannot read a calendar says so with
/// <see cref="CalendarRead.Unreadable"/> rather than returning an empty list.</para>
/// </summary>
public interface IHouseholdCalendars
{
    /// <summary>One adult's own calendar (their primary calendar).</summary>
    CalendarRead OwnCalendar(string person, DateTimeOffset from, DateTimeOffset to);

    /// <summary>The family calendar.</summary>
    CalendarRead FamilyCalendar(DateTimeOffset from, DateTimeOffset to);

    /// <summary>The children's calendars, taken together (their events, as the head merges them).</summary>
    CalendarRead ChildrensCalendars(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>
/// The calendars already fetched: an immutable in-memory <see cref="IHouseholdCalendars"/>. The head
/// builds one per planning pass; the tests build them from an invented household. It answers every
/// window with everything it holds (the model does the filtering), and answers a person it was
/// never given a calendar for with <i>unreadable</i>, not <i>empty</i>.
/// </summary>
public sealed class HouseholdCalendarSnapshot : IHouseholdCalendars
{
    private readonly IReadOnlyDictionary<string, CalendarRead> own;
    private readonly CalendarRead family;
    private readonly CalendarRead children;

    /// <summary>
    /// <paramref name="own"/> is keyed by the adult's name (matched without case). An adult missing from it
    /// reads as unreadable.
    /// </summary>
    public HouseholdCalendarSnapshot(IReadOnlyDictionary<string, CalendarRead> own, CalendarRead family, CalendarRead children)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(children);

        this.own = new Dictionary<string, CalendarRead>(own, StringComparer.OrdinalIgnoreCase);
        this.family = family;
        this.children = children;
    }

    public CalendarRead OwnCalendar(string person, DateTimeOffset from, DateTimeOffset to) =>
        own.TryGetValue(person, out var read)
            ? read
            : CalendarRead.Unreadable("No calendar was loaded for this person.");

    public CalendarRead FamilyCalendar(DateTimeOffset from, DateTimeOffset to) => family;

    public CalendarRead ChildrensCalendars(DateTimeOffset from, DateTimeOffset to) => children;
}
