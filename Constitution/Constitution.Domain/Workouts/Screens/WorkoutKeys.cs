using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>The calendar key each of a person's sessions carries, worked out the way placement does it (kind and place among its kind, in time order).</summary>
public static class WorkoutKeys
{
    public static IReadOnlyDictionary<PlannedSession, PlacementKey> Of(WorkoutWeek week, string person)
    {
        ArgumentNullException.ThrowIfNull(week);
        var plan = week.Plan.PlanFor(person)
            ?? throw new ArgumentException("That person has no plan in this week.", nameof(person));
        var weekIndex = PlacementKey.WeekIndexOf(week.Plan.WeekStart);
        var counts = new Dictionary<SessionKind, int>();
        var keys = new Dictionary<PlannedSession, PlacementKey>();
        foreach (var session in plan.Sessions.OrderBy(item => item.Start))
        {
            var kind = session.Session.Kind;
            var ordinal = counts.GetValueOrDefault(kind);
            counts[kind] = ordinal + 1;
            keys[session] = PlacementKey.ForWorkout(plan.Person, kind, weekIndex, ordinal);
        }

        return keys;
    }
}
