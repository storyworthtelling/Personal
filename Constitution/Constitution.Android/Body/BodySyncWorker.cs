using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Constitution.Domain;
using Constitution.Services;

namespace Constitution.Body;

/// <summary>
/// The overnight Body read (C-7.1.3). WorkManager wakes it about twice a day; with
/// <c>READ_HEALTH_DATA_IN_BACKGROUND</c> it reads what Health Connect holds so the morning recovery check is current,
/// and without it <see cref="Constitution.Domain.Body.BodySync"/> reads nothing and says why in the trail, because the
/// app refreshes the values each time it opens instead (R5).
///
/// <para><b>Both names are frozen</b> (<see cref="ConstitutionIdentity.BodySyncWorkerName"/>,
/// <see cref="ConstitutionIdentity.BodySyncWorkName"/>). WorkManager persists a scheduled row by Java class name and
/// periodic work is enqueued with <c>Keep</c>, so a moved class is one it can no longer build, and the job quietly stops.
/// A guard reads this file by content to hold the registration to the constant.</para>
///
/// <para>Device-gated: nothing here has met a phone. The decisions it leans on are unit-tested.</para>
/// </summary>
[Register(ConstitutionIdentity.BodySyncWorkerName)]
public class BodySyncWorker : Worker
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    public BodySyncWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        var context = ApplicationContext!;
        var log = new BodyLog(context);
        try
        {
            var services = BodyServices.Create(context);
            var sync = services.Sync.RunInBackgroundAsync(CancellationToken.None).GetAwaiter().GetResult();

            // The encrypted backup follows a read that stored something; its outcome is the runner's to log, and it
            // never fails the read it follows.
            BodyBackups.AfterWorkerSyncAsync(context, sync, services.Store, CancellationToken.None).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            // The type name is the whole report; the next period tries again on its own.
            log.Outcome($"body sync worker failed: {e.GetType().Name}");
            return Result.InvokeFailure();
        }
    }

    /// <summary>
    /// Queues the periodic read under the frozen unique name. <c>Keep</c> means a relaunch never disturbs the pending
    /// run. Called from the activity at start; it does nothing the second time.
    /// </summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiresBatteryNotLow(true)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(BodySyncWorker), Interval)
            .SetConstraints(constraints)!
            .AddTag(ConstitutionIdentity.BodySyncWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(ConstitutionIdentity.BodySyncWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }
}
