using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// Which calendar event belongs to whose day. Placement keys an event by person, kind, week and the session's place
/// "among this person's sessions of its kind, in time order", and its outcomes name keys, not sessions. The run needs the
/// other direction (this person, this day: where is their event, and was it deleted?), so this works the same keys out the
/// same way. <c>PlacementIndexTests</c> holds it to <see cref="PlacementEngine.Wanted"/>, so a change to the engine's
/// numbering fails there instead of quietly pairing a page with the wrong event.
///
/// <para>A person has at most one session a day (the planner and <see cref="PlanRules"/> promise it), so (person, day) names
/// a session. If a plan ever held two, the earlier one is named and both are counted in the ordinals, as placement does.</para>
/// </summary>
public sealed class PlacementIndex
{
    private readonly Dictionary<(string Person, DateOnly Day), PlacementKey> keys = [];

    public PlacementIndex(HouseholdWorkoutPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var week = PlacementKey.WeekIndexOf(plan.WeekStart);

        foreach (var weekly in plan.Plans)
        {
            var mine = weekly.Sessions
                .Concat(plan.JointSessions.Where(joint => joint.Participants.Any(name => Same(name, weekly.Person))))
                .DistinctBy(session => (session.Start, session.Session.Kind, session.Session.Duration, session.Together))
                .OrderBy(session => session.Start)
                .ToList();

            var counts = new Dictionary<SessionKind, int>();
            foreach (var session in mine)
            {
                var ordinal = counts.GetValueOrDefault(session.Session.Kind);
                counts[session.Session.Kind] = ordinal + 1;
                keys.TryAdd((Normal(weekly.Person), session.Day), PlacementKey.ForWorkout(weekly.Person, session.Session.Kind, week, ordinal));
            }
        }
    }

    /// <summary>The key of the person's session on that day, or null when they have none.</summary>
    public PlacementKey? KeyFor(string person, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(person);
        return keys.TryGetValue((Normal(person), day), out var key) ? key : null;
    }

    private static string Normal(string person) => person.Trim().ToLowerInvariant();

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
