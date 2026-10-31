using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Constitution.Body;
using Constitution.Domain;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.WeeklyMeals;

/// <summary>
/// The meal side's background pass (P13): once an hour, and once when the app opens, it asks <see cref="MealPassPlan"/> what is due and does only that. A pass
/// with nothing due reads three small local files and stops (no household file, calendar, tablet or network, no trail line); a due one runs the weekly meal run
/// (plan the coming week, place it, send the sheet) and then the Sunday-8:00 deadline step, each through the one run gate the workout run uses. All the work is
/// <see cref="MealWeekServices.PassAsync"/>; this is the WorkManager door to it.
///
/// <para><b>Both names are frozen</b> (<see cref="ConstitutionIdentity.MealWeekWorkerName"/>, <see cref="ConstitutionIdentity.MealWeekWorkName"/>), for the reason the workout
/// worker's are: WorkManager persists the row by Java class name and the work is enqueued with <c>Keep</c>. A guard reads this file by content.</para>
///
/// <para><b>Errors.</b> The pass types everything it can (a busy gate, an unreadable household file, a calendar that would not answer are all outcomes, not throws);
/// what is left is a bug or a full disk. Each part's failure is kept, logged by type name only, and the other part still runs; the worker then fails the pass
/// with the original exception (stack and all). Nothing here is swallowed: this worker is the end of the line, so the trail line and the failed result are the report.</para>
///
/// <para>Device-gated: WorkManager has not yet woken this worker on a phone. The decisions it leans on are unit-tested.</para>
/// </summary>
[Register(ConstitutionIdentity.MealWeekWorkerName)]
public class MealWeekWorker : Worker
{
    public MealWeekWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        var context = ApplicationContext!;
        try
        {
            // WorkManager calls this on its own background thread, so waiting here blocks nothing the owner sees.
            MealWeekServices.PassAsync(context, MealPassTrigger.Scheduled, CancellationToken.None).GetAwaiter().GetResult().ThrowIfFaulted();
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            // The type name is the whole report; the next hour tries again on its own.
            new BodyLog(context).Outcome($"meal week worker failed: {e.GetType().Name}");
            return Result.InvokeFailure();
        }
    }

    /// <summary>
    /// Queues the hourly pass under the frozen unique name, first at five past the next hour on the household's clock (<see cref="MealPassSchedule"/>).
    /// <c>Keep</c> means a relaunch never disturbs the pending run. Called from the activity at start, before the workout worker's.
    /// </summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var delay = (long)MealPassSchedule.DelayUntilNext(DateTimeOffset.Now, TimeZoneInfo.Local).TotalMilliseconds;
        var request = new PeriodicWorkRequest.Builder(typeof(MealWeekWorker), MealPassSchedule.Interval)
            .SetConstraints(constraints)!
            .SetInitialDelay(delay, Java.Util.Concurrent.TimeUnit.Milliseconds!)!
            .AddTag(ConstitutionIdentity.MealWeekWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(ConstitutionIdentity.MealWeekWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    /// <summary>
    /// The pass on app open. Off the main thread: it may read calendars and the tablet. There is no in-memory debounce here: the stamp the pass keeps is the debounce,
    /// so an open after a day that planned (or found this phone is not the planner) is the three local reads and nothing else, and an open after a plan that could
    /// not settle tries again at once, for whoever has just set something up.
    /// </summary>
    public static void RunOnOpen(Context context)
    {
        var application = context.ApplicationContext!;
        _ = Task.Run(async () =>
        {
            try
            {
                (await MealWeekServices.PassAsync(application, MealPassTrigger.Opened, CancellationToken.None)).ThrowIfFaulted();
            }
            catch (Exception e)
            {
                new BodyLog(application).Outcome($"meal week worker failed: {e.GetType().Name}");
            }
        });
    }
}
