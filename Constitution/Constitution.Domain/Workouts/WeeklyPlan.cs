namespace Constitution.Domain.Workouts;

/// <summary>One person's week: their sessions in time order, or nothing and the plain reason why.</summary>
public sealed record WeeklyPlan(string Person, DateOnly WeekStart, IReadOnlyList<PlannedSession> Sessions, string? NothingBecause)
{
    public IReadOnlyList<PlannedSession> SessionsOn(DateOnly day) => Sessions.Where(session => session.Day == day).ToList();
}

/// <summary>The week for everyone asked about, plus the sessions that are shared.</summary>
public sealed record HouseholdWorkoutPlan(DateOnly WeekStart, IReadOnlyList<WeeklyPlan> Plans, IReadOnlyList<PlannedSession> JointSessions)
{
    /// <summary>The sessions that belong on the family calendar: the joint ones.</summary>
    public IReadOnlyList<PlannedSession> FamilyCalendarSessions => JointSessions;

    public WeeklyPlan? PlanFor(string person) =>
        Plans.FirstOrDefault(plan => string.Equals(plan.Person, person?.Trim(), StringComparison.OrdinalIgnoreCase));
}
