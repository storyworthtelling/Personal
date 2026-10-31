namespace Constitution.Domain.Workouts;

/// <summary>
/// The week for everyone, with each person's phase, the assessment sessions as tests, and (when anyone was planned
/// the ordinary way) how that part was authored.
/// </summary>
public sealed record PhasedWeekPlan(
    HouseholdWorkoutPlan Plan,
    IReadOnlyDictionary<string, PhaseDecision> Phases,
    IReadOnlyList<AssessmentSession> Assessments,
    PlanOutcome? Moderate);

/// <summary>
/// Joins the phases to the planner. Each person's phase is decided from their own history
/// (<see cref="WorkoutPhaseRules"/>); anyone in the assessment is planned by <see cref="AssessmentPlanner"/>, always
/// by rules (its content is fixed by the brief, so an author is never asked), and everyone else is planned together by
/// <paramref name="planner"/> (the author if reachable, the rules if not). Joint sessions are made only among the
/// people planned the ordinary way, so someone in their assessment week is in none.
///
/// <para>A person with no entry in the histories is refused, not assumed fresh: "no history" is a first install and
/// would quietly send an established person back to the assessment.</para>
/// </summary>
public static class PhasedWeek
{
    public static async Task<PhasedWeekPlan> PlanAsync(
        WorkoutPlanRequest request,
        IReadOnlyDictionary<string, TrainingHistory> histories,
        PlanWithFallback planner,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentNullException.ThrowIfNull(planner);
        if (request.People is null || request.People.Count == 0)
        {
            throw new ArgumentException("There is no one to plan for.", nameof(request));
        }

        var phases = new Dictionary<string, PhaseDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in request.People)
        {
            var name = person.Busy.Person;
            var history = histories.FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase));
            if (history.Value is null)
            {
                throw new ArgumentException($"{name} has no training history in the request; pass TrainingHistory.Fresh for a first install.", nameof(histories));
            }

            phases[name] = WorkoutPhaseRules.Decide(history.Value, request.WeekStart);
        }

        var assessed = request.People.Where(person => phases[person.Busy.Person].Phase == WorkoutPhase.Assessment).ToList();
        var ordinary = request.People.Where(person => phases[person.Busy.Person].Phase == WorkoutPhase.Moderate).ToList();

        var assessment = assessed.Count > 0 ? AssessmentPlanner.Plan(request with { People = assessed }) : null;
        var moderate = ordinary.Count > 0 ? await planner.PlanAsync(request with { People = ordinary }, cancellation) : null;

        var plans = request.People
            .Select(person => (assessment?.Plan.PlanFor(person.Busy.Person) ?? moderate?.Plan.PlanFor(person.Busy.Person))!)
            .ToList();
        var plan = new HouseholdWorkoutPlan(request.WeekStart, plans, moderate?.Plan.JointSessions ?? []);
        return new PhasedWeekPlan(plan, phases, assessment?.Sessions ?? [], moderate);
    }
}
