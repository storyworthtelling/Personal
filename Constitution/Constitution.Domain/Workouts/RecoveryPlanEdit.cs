namespace Constitution.Domain.Workouts;

/// <summary>
/// Rewrites a week's sessions without breaking what the planner promised about joint ones: a joint session is one object
/// in every participant's plan and in <see cref="HouseholdWorkoutPlan.JointSessions"/>, so each distinct session is edited
/// once and the result shared everywhere it appeared. A plan nothing changed in comes back as the same object, so "left
/// exactly as planned" is something a caller (and a test) can see.
/// </summary>
internal static class RecoveryPlanEdit
{
    /// <summary>
    /// <paramref name="edit"/> returns the session unchanged (the same object), a replacement, or null to take the
    /// session out of the week. <paramref name="emptyBecause"/> is the plain sentence for a person whose week is empty
    /// only because of what was taken out.
    /// </summary>
    public static HouseholdWorkoutPlan Rewrite(HouseholdWorkoutPlan plan, string emptyBecause, Func<PlannedSession, PlannedSession?> edit)
    {
        var edited = new Dictionary<PlannedSession, PlannedSession?>(ReferenceEqualityComparer.Instance);
        var changed = false;

        PlannedSession? Edit(PlannedSession session)
        {
            if (!edited.TryGetValue(session, out var result))
            {
                result = edit(session);
                edited[session] = result;
                changed |= !ReferenceEquals(result, session);
            }

            return result;
        }

        var plans = plan.Plans.Select(week =>
        {
            var sessions = week.Sessions.Select(Edit).OfType<PlannedSession>().ToList();
            var removedAny = sessions.Count < week.Sessions.Count;
            return week with
            {
                Sessions = sessions,
                NothingBecause = sessions.Count == 0 && removedAny ? emptyBecause : week.NothingBecause,
            };
        }).ToList();
        var joint = plan.JointSessions.Select(Edit).OfType<PlannedSession>().ToList();

        return changed ? plan with { Plans = plans, JointSessions = joint } : plan;
    }
}
