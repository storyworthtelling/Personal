namespace Constitution.Domain.Placement;

/// <summary>An event the calendar holds under one of our keys, wherever it now is. Its times are what the calendar says, which is the owner's last word.</summary>
public sealed record HeldEvent(string EventId, PlacementKey Key, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// What looking events up by key gave: the events found, or the reason the calendar could not be looked into.
/// The two are never confused. A calendar nobody could look into (the partner has not shared hers yet, Google
/// would not answer) is <b>not</b> a calendar with none of our events on it: the second would read as "the owner
/// deleted everything" and the first is not knowing.
/// </summary>
public sealed class CalendarLookup
{
    private CalendarLookup(IReadOnlyList<HeldEvent> held, string? unavailableBecause)
    {
        Held = held;
        UnavailableBecause = unavailableBecause;
    }

    /// <summary>The events found; empty when the calendar was unavailable.</summary>
    public IReadOnlyList<HeldEvent> Held { get; }

    /// <summary>Why the calendar could not be looked into, or null when it could.</summary>
    public string? UnavailableBecause { get; }

    public bool IsAvailable => UnavailableBecause is null;

    /// <summary>A lookup that worked. It may have found nothing; that then means the events are not there.</summary>
    public static CalendarLookup Found(IEnumerable<HeldEvent> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        return new CalendarLookup(held.ToList().AsReadOnly(), null);
    }

    /// <summary>A lookup that could not be made. The reason is a plain sentence with no calendar content in it.</summary>
    public static CalendarLookup Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unavailable calendar has to say why it could not be looked into.", nameof(reason));
        }

        return new CalendarLookup([], reason.Trim());
    }
}

/// <summary>What creating one event gave: its id, or the reason it was not made.</summary>
public sealed class CalendarCreate
{
    private CalendarCreate(string? eventId, string? failedBecause)
    {
        EventId = eventId;
        FailedBecause = failedBecause;
    }

    public string? EventId { get; }

    public string? FailedBecause { get; }

    public bool Succeeded => FailedBecause is null;

    public static CalendarCreate Created(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("A created event has an id.", nameof(eventId));
        }

        return new CalendarCreate(eventId, null);
    }

    public static CalendarCreate Failed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed write has to say why.", nameof(reason));
        }

        return new CalendarCreate(null, reason.Trim());
    }
}

/// <summary>
/// The seam the Android head fills from Google Calendar, on the person's <b>primary</b> calendar and with the
/// full <c>calendar</c> scope its sign-in already asks for.
///
/// <para><b>It can find and it can create, and that is all, on purpose.</b> There is no move, no update and no
/// delete, so the planner has no way to take an event back from the owner. "The owner's edits win, silently" is
/// then a fact about the shape of this interface rather than a promise about the code behind it
/// (<c>PlacementSeamTests.TheWriterCanOnlyFindAndCreate</c>). When Skip and Swap need to remove an event they
/// will add their own call, with its own tests, as a decision.</para>
///
/// <para><b>Find</b> looks the keys up by the private extended property <see cref="PlacementKey.Property"/>
/// across the whole calendar, with no time bounds, so an event the owner dragged to another week is still found.
/// It returns <see cref="CalendarLookup.Unavailable"/> whenever it cannot say, and a clean
/// <see cref="CalendarLookup.Found"/> only when the answer is complete; an answer that might be partial is
/// "unavailable", never "found". <b>Create</b> makes one event titled <c>Title</c> over <c>Start</c> to
/// <c>End</c>, the key in the private extended property, with no guests, no description and no notification
/// to anyone. Expected trouble (no write access to a shared calendar) comes back as
/// <see cref="CalendarCreate.Failed"/>; anything else is thrown with its own message, not swallowed.</para>
/// </summary>
public interface ICalendarWriter
{
    Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken);

    Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken);
}
