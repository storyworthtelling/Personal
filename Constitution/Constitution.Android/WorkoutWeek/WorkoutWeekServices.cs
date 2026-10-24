using Android.Content;
using Constitution.Body;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.WorkoutWeek;

/// <summary>
/// The door from the head to the weekly workout run. THIS IS A SKELETON: it reports the typed "not configured" result and
/// leaves its value-free line in the Body trail, so the worker, the schedule and the open path can be proven in place before
/// the run is wired. The signature of <see cref="RunAsync"/> is the contract the worker calls and must not change.
///
/// <para>Device-gated: nothing here has run on a phone.</para>
/// </summary>
public static class WorkoutWeekServices
{
    public static Task<WorkoutWeekOutcome> RunAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();

        // SEAMS GO HERE. The job that fills this in assembles the run's seams (WorkoutWeekSeams: the calendars, the records,
        // the placement, the tablet delivery, the notice board) from the same stores the rest of the head uses, calls
        // Infrastructure.Workouts.WorkoutWeekRun with them, and returns its outcome. Until then there is nothing to read from.
        WorkoutWeekOutcome outcome = new WorkoutWeekOutcome.NotPlanned(
            NotPlannedBecause.NotConfigured, [], "The weekly workout run has no sources assembled on this install yet.");

        new BodyLog(context).Outcome(WorkoutWeekTrail.LogPhrase(outcome));
        return Task.FromResult(outcome);
    }
}
