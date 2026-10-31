using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Constitution.Body;
using Constitution.Domain;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.SaturdayDigest;

/// <summary>
/// The Saturday digest's background door (Release E): an hourly pass that costs one small local read on any day but Saturday and the Sunday after, and on those days
/// builds, draws, delivers and stamps the digest once for the adult whose tablet this phone has. All the work is <see cref="DigestServices.TryRunAsync"/>, which goes
/// through the shared run gate; this is the WorkManager door to it, and the activity also runs it once on open.
///
/// <para><b>Both names are frozen</b> (<see cref="ConstitutionIdentity.DigestWorkerName"/>, <see cref="ConstitutionIdentity.DigestWorkName"/>), for the reason the other workers' are:
/// WorkManager persists a scheduled row by Java class name and the work is enqueued with <c>Keep</c>. A guard reads this file by content.</para>
///
/// <para><b>Errors.</b> A busy gate, a store that would not answer and a tablet that refused are typed outcomes, not throws. What is left is a bug or a full disk: it is logged by type
/// name only and the pass fails with the original exception's type in the trail. Nothing is swallowed.</para>
///
/// <para>Device-gated: WorkManager has not yet woken this worker on a phone.</para>
/// </summary>
[Register(ConstitutionIdentity.DigestWorkerName)]
public class DigestWorker : Worker
{
    public DigestWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        var context = ApplicationContext!;
        Constitution.Waiting.NoticePoster.Attach(context);
        try
        {
            // WorkManager calls this on its own background thread, so waiting here blocks nothing the owner sees.
            DigestServices.TryRunAsync(context, CancellationToken.None).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            new BodyLog(context).Outcome($"digest worker failed: {e.GetType().Name}");
            return Result.InvokeFailure();
        }
    }

    /// <summary>Queues the hourly request under its frozen unique name with <c>Keep</c>, so a relaunch never disturbs a pending run.</summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(DigestWorker), MealPassSchedule.Interval)
            .SetConstraints(constraints)!
            .SetInitialDelay((long)MealPassSchedule.DelayUntilNext(DateTimeOffset.Now, TimeZoneInfo.Local).TotalMilliseconds, Java.Util.Concurrent.TimeUnit.Milliseconds!)!
            .AddTag(ConstitutionIdentity.DigestWorkName)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(ConstitutionIdentity.DigestWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    /// <summary>The pass on app open, off the main thread. The stamp is the debounce: an open after the week's digest went is one small read.</summary>
    public static void RunOnOpen(Context context)
    {
        var application = context.ApplicationContext!;
        _ = Task.Run(async () =>
        {
            try
            {
                await DigestServices.TryRunAsync(application, CancellationToken.None);
            }
            catch (Exception e)
            {
                new BodyLog(application).Outcome($"digest worker failed: {e.GetType().Name}");
            }
        });
    }
}
