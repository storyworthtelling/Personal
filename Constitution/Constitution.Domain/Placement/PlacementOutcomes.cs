namespace Constitution.Domain.Placement;

/// <summary>Why an event was not placed. Said in a type so the head can show it plainly and nothing is guessed.</summary>
public enum UnplacedReason
{
    /// <summary>The event does not fit anywhere it is allowed to go, even without its shower.</summary>
    NoRoom,

    /// <summary>The session fits but the shower after it does not. Not placed, rather than placed on top of something.</summary>
    NoRoomForShowerBuffer,

    /// <summary>Someone's calendar could not be read, so their free time is not known and nothing is placed on a guess.</summary>
    CalendarUnreadable,

    /// <summary>Someone's calendar could not be looked into for our events (a share not done yet), so nothing is written or concluded.</summary>
    CalendarUnavailable,

    /// <summary>
    /// A key on the calendar or in the record of written events cannot be tied to this session by anything that was issued, so
    /// the week's numbering for this person and kind is in doubt and nothing is written, skipped or paged for it on a guess.
    /// </summary>
    KeysUncertain,
}

/// <summary>What placement decided about one event on one person's calendar.</summary>
public abstract record PlacementOutcome(PlacementKey Key, string Person, string Title);

/// <summary>New: not on the calendar, never written, and it fits. <see cref="Start"/> to <see cref="End"/> is the event itself, not its shower.</summary>
public sealed record EventToCreate(PlacementKey Key, string Person, string Title, DateTimeOffset Start, DateTimeOffset End)
    : PlacementOutcome(Key, Person, Title);

/// <summary>
/// Already on the calendar, at <see cref="Start"/> to <see cref="End"/> as the calendar says. That time stands
/// whether it is where it was written or where the owner has since put it: the plan takes it and writes nothing.
/// </summary>
public sealed record EventHeld(PlacementKey Key, string Person, string Title, DateTimeOffset Start, DateTimeOffset End)
    : PlacementOutcome(Key, Person, Title);

/// <summary>Written once and no longer on the calendar: the owner deleted it. The session is skipped and is not placed again.</summary>
public sealed record EventSkipped(PlacementKey Key, string Person, string Title)
    : PlacementOutcome(Key, Person, Title);

/// <summary>Could not be placed, and why. Nothing is written for it.</summary>
public sealed record EventUnplaced(PlacementKey Key, string Person, string Title, UnplacedReason Reason)
    : PlacementOutcome(Key, Person, Title);

/// <summary>Every event asked for, with what became of it, in the order it was considered.</summary>
public sealed record PlacementPlan(IReadOnlyList<PlacementOutcome> Outcomes)
{
    /// <summary>
    /// The workout numbers this plan issued, for the caller to keep before it writes anything: sessions the record of issued
    /// numbers did not know, whose person and kind are not in doubt and whose calendar was looked into. Empty when the request
    /// carried no record, since a caller that keeps none has nothing to keep.
    /// </summary>
    public IReadOnlyList<KeyIssue> Issued { get; init; } = [];

    /// <summary>
    /// The people and kinds the plan would not decide anything for, and why (<see cref="UnplacedReason.KeysUncertain"/>): the
    /// typed result of the engine refusing to guess. Each one is a person's sessions of one kind in the week.
    /// </summary>
    public IReadOnlyList<UncertainKind> UncertainKinds { get; init; } = [];

    public IReadOnlyList<EventToCreate> ToCreate => Outcomes.OfType<EventToCreate>().ToList();

    public IReadOnlyList<EventHeld> Held => Outcomes.OfType<EventHeld>().ToList();

    public IReadOnlyList<EventSkipped> Skipped => Outcomes.OfType<EventSkipped>().ToList();

    public IReadOnlyList<EventUnplaced> Unplaced => Outcomes.OfType<EventUnplaced>().ToList();

    /// <summary>What became of the event with that key, or null if it was not asked for.</summary>
    public PlacementOutcome? For(PlacementKey key) => Outcomes.FirstOrDefault(outcome => outcome.Key == key);
}

/// <summary>One person's sessions of one kind that a plan left undecided, and the plain reason (no name, time or key in it).</summary>
public sealed record UncertainKind(string Person, Constitution.Domain.Workouts.SessionKind Kind, string Why);
