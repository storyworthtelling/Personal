namespace Constitution.Domain.Workouts;

/// <summary>
/// The rules engine behind the Workouts tab (C-5.1.1): a balanced week of strength, cardio, mobility and a
/// Sunday walk for each adult, sized to the free gaps in their calendar and to the equipment ticked in
/// Settings. Pure: everything it reads is in the <see cref="WorkoutPlanRequest"/> (the clock included), and the
/// same request always gives the same plan.
///
/// <para><b>What it promises.</b> A session sits inside a gap of at least <see cref="MinimumGap"/> that
/// <see cref="Constitution.Domain.Calendar.BusyTime"/> reports free, inside waking hours (sleep is not in
/// never-placed time, so the bound is the planner's); never prescribes an unticked item (D72); puts nothing
/// on a Sunday but a gentle walk; never puts two hard sessions on consecutive days or two sessions on
/// one day; and fails closed: a person whose calendars could not all be read gets no sessions and a sentence
/// saying why. A family walk (the children awake) or an adult session after the children's bedtime is a
/// <i>joint</i> session, one object in every participant's plan that replaces their own that day, and the one
/// to put on the family calendar. The evening is used only for joint sessions.</para>
///
/// <para><b>What it leaves to others.</b> Where in a gap the session finally goes, the shower buffer after a
/// sweaty one, and the title and hidden key are the placement step's (C-5.3.2); it reads
/// <see cref="PlannedSession.Gap"/>. Progression, weather, soreness, recovery signals and trips are later
/// PBIs and arrive through <see cref="RecoveryInputs"/> and new inputs, not hidden state.</para>
/// </summary>
public static class WorkoutPlanner
{
    /// <summary>The smallest free gap a session may be placed in.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(30);

    public static HouseholdWorkoutPlan Plan(WorkoutPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new WeekPlanner(request).Plan();
    }
}
