using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>
/// The calendar key each of a person's sessions carries, worked out the way placement does it (<see cref="SessionKeys"/>: the
/// number issued to the session, the next one above the highest if it has none). <b>Hand it the record of issued numbers the weekly
/// run keeps</b>, or the names will be the old count of the nth session of its kind, which stops meaning the same event the first
/// time the week is re-planned.
/// </summary>
public static class WorkoutKeys
{
    public static IReadOnlyDictionary<PlannedSession, PlacementKey> Of(WorkoutWeek week, string person, KeyAssignments? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(week);
        return Of(week.Plan, person, assignments);
    }

    /// <summary>The same keys for a plan on its own.</summary>
    public static IReadOnlyDictionary<PlannedSession, PlacementKey> Of(HouseholdWorkoutPlan household, string person, KeyAssignments? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(household);
        var plan = household.PlanFor(person)
            ?? throw new ArgumentException("That person has no plan in this week.", nameof(person));
        var numbered = SessionKeys.For(household, assignments);
        var keys = new Dictionary<PlannedSession, PlacementKey>();
        foreach (var session in plan.Sessions.OrderBy(item => item.Start))
        {
            keys[session] = numbered.KeyOf(plan.Person, session)
                ?? throw new InvalidOperationException("A session in this person's plan does not name them as a participant, so it has no key for them.");
        }

        return keys;
    }

    /// <summary>The key of a session of this person in whichever of these weeks holds its day.</summary>
    public static PlacementKey In(IReadOnlyList<WorkoutWeek> weeks, string person, PlannedSession session, KeyAssignments? assignments = null) =>
        IssueIn(weeks, person, session, assignments).Key;

    /// <summary>The number the person's session is (or would be) issued, in whichever of these weeks holds its day.</summary>
    public static KeyIssue IssueIn(IReadOnlyList<WorkoutWeek> weeks, string person, PlannedSession session, KeyAssignments? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(session);
        var week = weeks.First(item => session.Day >= item.Plan.WeekStart && session.Day < item.Plan.WeekStart.AddDays(7));
        return SessionKeys.For(week.Plan, assignments).IssueOf(person, session)
            ?? throw new InvalidOperationException("That session is not in the person's plan for its week, so it has no key.");
    }
}
