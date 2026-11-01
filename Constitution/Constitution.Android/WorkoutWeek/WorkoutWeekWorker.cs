using Codex.Ui;
using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Constitution.Body;
using Constitution.Domain;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.WorkoutWeek;

/// <summary>
/// The daily workout-week run (P13): the week planned, placed and sent a quarter to seven on the owner's clock, after Foreword's
/// delivery and before the read-back. All the work is <see cref="WorkoutWeekServices"/>; this is the WorkManager door to it.
///
/// <para><b>Both names are frozen</b> (<see cref="ConstitutionIdentity.WorkoutWeekWorkerName"/>,
/// <see cref="ConstitutionIdentity.WorkoutWeekWorkName"/>), for the reason the read-back's are: WorkManager persists the row by
/// Java class name and the work is enqueued with <c>Keep</c>. A guard reads this file by content.</para>
///
/// <para>Device-gated: nothing here has run on a phone. The decisions it leans on are unit-tested.</para>
/// </summary>
[Register(ConstitutionIdentity.WorkoutWeekWorkerName)]
public class WorkoutWeekWorker : Worker
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    // When the last run that planned the week finished, as UTC ticks (0 = none). In memory: an open after the process was
    // reclaimed simply runs, and the run is idempotent.
    private static long lastPlannedTicks;

    public WorkoutWeekWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(WorkoutWeekWorker), Work);

    private Result Work()
    {
        var context = ApplicationContext!;
        Constitution.Waiting.NoticePoster.Attach(context);
        try
        {
            // WorkManager calls this on its own background thread, so waiting here blocks nothing the owner sees.
            Remember(WorkoutWeekServices.RunAsync(context, CancellationToken.None).GetAwaiter().GetResult());
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            // The type name is the whole report; the next morning tries again on its own.
            new BodyLog(context).Outcome($"workout week worker failed: {e.GetType().Name}");
            return Result.InvokeFailure();
        }
    }

    /// <summary>
    /// Queues the daily run under the frozen unique name, first at the time <see cref="WorkoutWeekSchedule"/> gives.
    /// <c>Keep</c> means a relaunch never disturbs the pending run. Called from the activity at start.
    /// </summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var delay = (long)WorkoutWeekSchedule.DelayUntilNext(DateTimeOffset.Now, TimeZoneInfo.Local).TotalMilliseconds;
        var request = new PeriodicWorkRequest.Builder(typeof(WorkoutWeekWorker), Interval)
            .SetConstraints(constraints)!
            .SetInitialDelay(delay, Java.Util.Concurrent.TimeUnit.Milliseconds!)!
            .AddTag(ConstitutionIdentity.WorkoutWeekWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(ConstitutionIdentity.WorkoutWeekWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    /// <summary>
    /// The run on app open, when <see cref="WorkoutWeekDebounce"/> says the last plan is stale. Off the main thread: the run
    /// reads calendars and the tablet.
    /// </summary>
    public static void RunOnOpen(Context context)
    {
        var last = Interlocked.Read(ref lastPlannedTicks);
        DateTimeOffset? lastPlanned = last == 0 ? null : new DateTimeOffset(last, TimeSpan.Zero);
        if (!WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, lastPlanned, DateTimeOffset.UtcNow))
        {
            return;
        }

        var application = context.ApplicationContext!;
        _ = Task.Run(async () =>
        {
            try
            {
                Remember(await WorkoutWeekServices.RunAsync(application, CancellationToken.None));
            }
            catch (Exception e)
            {
                new BodyLog(application).Outcome($"workout week worker failed: {e.GetType().Name}");
            }
        });
    }

    private static void Remember(WorkoutWeekOutcome outcome)
    {
        if (outcome is WorkoutWeekOutcome.Planned)
        {
            Interlocked.Exchange(ref lastPlannedTicks, DateTimeOffset.UtcNow.UtcTicks);
        }
    }
}
