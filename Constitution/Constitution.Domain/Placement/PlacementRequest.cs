using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>
/// Everything one placement pass reads, passed in: no clock, no store, no hidden state. The same request and the
/// same <see cref="CalendarState"/> always give the same plan.
/// </summary>
/// <param name="WeekStart">The first day of the week being placed. Only its week number is used (see <see cref="PlacementKey.WeekIndexOf"/>).</param>
/// <param name="People">Each adult's busy and never-placed time for the week (<see cref="BusyTime"/>), one per person. Anyone named by an event or a session must be here.</param>
/// <param name="HouseholdEvents">Meals, shopping and the like, in the order they claim time: earlier ones win a contest for the same minutes.</param>
/// <param name="Workouts">The planner's week, or null when only household events are being placed. Its week must be the same week.</param>
public sealed record PlacementRequest(
    DateOnly WeekStart,
    IReadOnlyList<BusyTime> People,
    IReadOnlyList<HouseholdEventRequest> HouseholdEvents,
    HouseholdWorkoutPlan? Workouts)
{
    public PlacementOptions Options { get; init; } = new();

    /// <summary>
    /// The workout numbers issued so far (<see cref="KeyAssignments"/>). With a record the engine gives every session the key it
    /// was issued, numbers new ones above the highest ever issued, and refuses to decide for a person and kind whose keys the
    /// record cannot vouch for; the week's re-plan then cannot move a session onto another's key. <b>Null means the caller keeps
    /// no record</b>: workouts are numbered by their place among their kind and nothing is judged, which is only right for a
    /// caller that plans a week once (the weekly run always passes one).
    /// </summary>
    public KeyAssignments? Assignments { get; init; }
}
