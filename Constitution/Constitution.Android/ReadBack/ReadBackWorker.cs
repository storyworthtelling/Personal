using Codex.Ui;
using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Constitution.Domain;
using Constitution.Domain.ReadBack;

namespace Constitution.ReadBack;

/// <summary>
/// The morning read-back (C-2.2.3, D87): yesterday's ink read off the tablet after Foreword's delivery window, resolved,
/// saved, and the page trashed. All the work is <see cref="Infrastructure.ReadBack.ReadBackService"/>; this is the
/// WorkManager door to it and the line it leaves in the trail.
///
/// <para><b>Both names are frozen</b> (<see cref="ConstitutionIdentity.ReadBackWorkerName"/>,
/// <see cref="ConstitutionIdentity.ReadBackWorkName"/>). WorkManager persists a scheduled row by Java class name and the
/// periodic work is enqueued with <c>Keep</c>, so a moved class is one it can no longer build and the mornings quietly stop.
/// A guard reads this file by content to hold the registration to the constant.</para>
///
/// <para>Device-gated: nothing here has met a tablet, the cloud or a vision reply. The decisions it leans on are unit-tested.</para>
/// </summary>
[Register(ConstitutionIdentity.ReadBackWorkerName)]
public class ReadBackWorker : Worker
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public ReadBackWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(ReadBackWorker), Work);

    private Result Work()
    {
        var context = ApplicationContext!;
        Constitution.Waiting.NoticePoster.Attach(context);
        var log = new ReadBackLog(context);
        try
        {
            var services = ReadBackServices.Create(context);
            var report = services.Service.Run(CancellationToken.None).GetAwaiter().GetResult();
            services.Status.Record(report.Outcome);
            log.Outcome($"ink read back worker ran, {report.Documents.Count} documents");
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            // The type name is the whole report; the next morning tries again on its own.
            log.Outcome($"ink read back worker failed: {e.GetType().Name}");
            return Result.InvokeFailure();
        }
    }

    /// <summary>
    /// Queues the daily read under the frozen unique name, first at the time <see cref="ReadBackSchedule"/> gives (after
    /// Foreword's delivery). <c>Keep</c> means a relaunch never disturbs the pending run. Called from the activity at start.
    /// </summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var delay = (long)ReadBackSchedule.DelayUntilNext(DateTimeOffset.Now).TotalMilliseconds;
        var request = new PeriodicWorkRequest.Builder(typeof(ReadBackWorker), Interval)
            .SetConstraints(constraints)!
            .SetInitialDelay(delay, Java.Util.Concurrent.TimeUnit.Milliseconds!)!
            .AddTag(ConstitutionIdentity.ReadBackWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(ConstitutionIdentity.ReadBackWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }
}
