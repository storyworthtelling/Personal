using Android.Content;
using AndroidX.Work;
using Storyworthtelling.Diagnostics;

namespace Codex.Ui;

/// <summary>
/// The weekly log upload for every native head but Post (LOG-LOOP-PLAN.md §3.3, O12): daily work that acts only
/// inside <see cref="UploadWindow"/> — from Saturday 18:00 on the phone's clock until a confirmed upload lands —
/// so a phone offline on Saturday uploads at its first connection. The work is <see cref="HeadDiagnostics.UploadAsync"/>.
/// <para>
/// The registered Java name and the unique work name are persisted by WorkManager with
/// <c>ExistingPeriodicWorkPolicy.Keep</c> in every app that schedules it; moving either strands the schedule,
/// so both are guarded. It lives in the shared kit, so each app runs its own copy under the same names.
/// </para>
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.shared.LoopUploadWorker")]
public class LoopUploadWorker : Worker
{
    public const string WorkName = "log-upload";

    public LoopUploadWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(LoopUploadWorker), TimeSpan.FromDays(1))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(LoopUploadWorker), Upload);

    private static Result Upload()
    {
        var head = HeadDiagnostics.Current;
        if (head is null || !UploadWindow.Due(head.History.LastConfirmed, DateTimeOffset.Now))
        {
            return Result.InvokeSuccess();
        }
        // A failed run is still a finished one: tomorrow's run is the retry, recorded by the head.
        head.UploadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Result.InvokeSuccess();
    }
}
