using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts;

/// <summary>Why a session belongs to more than one person.</summary>
public enum Together
{
    /// <summary>One person's own session.</summary>
    None,

    /// <summary>A family walk: the children are awake and come too.</summary>
    FamilyWalk,

    /// <summary>The adults do it together once the children are in bed.</summary>
    AfterBedtime,
}

/// <summary>
/// A session placed on a day. <see cref="Gap"/> is the free stretch it was found in and
/// <see cref="Start"/> is where in that stretch the planner proposes it; the calendar-placement step
/// owns the final minute, so it must stay inside <see cref="Gap"/>. A joint session is one object shared
/// by every participant's plan.
/// </summary>
public sealed record PlannedSession(
    DateOnly Day,
    DateTimeOffset Start,
    FreeGap Gap,
    Session Session,
    IReadOnlyList<string> Participants,
    Together Together)
{
    public DateTimeOffset End => Start + Session.Duration;

    /// <summary>A joint session goes on the family calendar and on every participant's card.</summary>
    public bool IsJoint => Together != Together.None;
}
