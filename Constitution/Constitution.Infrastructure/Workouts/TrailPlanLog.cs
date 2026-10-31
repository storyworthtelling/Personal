using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Sends the planner's origin line ("workout plan: rules only (not configured)", from <see cref="PlanDiagnostics"/>, which
/// is already a fixed vocabulary) into the weekly run's trail. The line is lower-cased because the export's filter reads a
/// capital as a possible name.
/// </summary>
public sealed class TrailPlanLog(IWorkoutWeekLog log) : IPlanLog
{
    private readonly IWorkoutWeekLog log = log ?? throw new ArgumentNullException(nameof(log));

    public void Record(PlanOrigin origin, AuthorUnavailable? reason) =>
        log.Outcome(PlanDiagnostics.Line(origin, reason).ToLowerInvariant());
}
