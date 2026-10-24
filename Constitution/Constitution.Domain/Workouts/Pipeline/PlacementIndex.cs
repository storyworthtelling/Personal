using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// Which calendar event belongs to whose day. Placement keys an event by person, kind, week and the number issued to the
/// session (<see cref="SessionKeys"/>), and its outcomes name keys, not sessions. The run needs the other direction (this
/// person, this day: where is their event, and was it deleted?), so this names the same keys the same way, by the same record
/// of issued numbers. <c>PlacementIndexTests</c> and <c>StableIndexTests</c> hold it to <see cref="PlacementEngine.Wanted"/>, so a
/// change to the engine's numbering fails there instead of quietly pairing a page with the wrong event.
///
/// <para>A person has at most one session a day (the planner and <see cref="PlanRules"/> promise it), so (person, day) names
/// a session. If a plan ever held two, the earlier one is named and both are numbered, as placement does.</para>
/// </summary>
public sealed class PlacementIndex
{
    private readonly SessionKeys keys;

    /// <param name="plan">The week as it is placed.</param>
    /// <param name="assignments">The numbers issued so far; null (or none) counts each kind from zero in time order, which is only right for a week placed once.</param>
    public PlacementIndex(HouseholdWorkoutPlan plan, KeyAssignments? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        keys = SessionKeys.For(plan, assignments);
    }

    /// <summary>The key of the person's session on that day, or null when they have none.</summary>
    public PlacementKey? KeyFor(string person, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(person);
        return keys.KeyOn(person, day);
    }
}
