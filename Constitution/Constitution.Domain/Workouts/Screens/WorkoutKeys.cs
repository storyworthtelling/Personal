using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>The calendar key each of a person's sessions carries, worked out the way placement does it (kind and place among its kind, in time order).</summary>
public static class WorkoutKeys
{
    public static IReadOnlyDictionary<PlannedSession, PlacementKey> Of(WorkoutWeek week, string person)
    {
        ArgumentNullException.ThrowIfNull(week);
        return Of(week.Plan, person);
    }

    /// <summary>The same keys for a plan on its own.</summary>
    public static IReadOnlyDictionary<PlannedSession, PlacementKey> Of(HouseholdWorkoutPlan household, string person)
    {
        ArgumentNullException.ThrowIfNull(household);
        var plan = household.PlanFor(person)
            ?? throw new ArgumentException("That person has no plan in this week.", nameof(person));
        var weekIndex = PlacementKey.WeekIndexOf(household.WeekStart);
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

    /// <summary>The key of a session of this person in whichever of these weeks holds its day.</summary>
    public static PlacementKey In(IReadOnlyList<WorkoutWeek> weeks, string person, PlannedSession session)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(session);
        var week = weeks.First(item => session.Day >= item.Plan.WeekStart && session.Day < item.Plan.WeekStart.AddDays(7));
        return Of(week, person)[session];
    }
}
