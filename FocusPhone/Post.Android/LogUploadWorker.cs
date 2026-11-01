using Android.Content;
using AndroidX.Work;
using Codex.Ui;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;

namespace Post;

/// <summary>
/// The weekly log upload (LOG-LOOP-PLAN.md §3.3, O12). Runs daily; each run asks
/// <see cref="UploadWindow.Due"/> whether this week's upload has happened yet — yes from Saturday 18:00 on
/// the phone's clock until one is confirmed — so a phone offline on Saturday uploads at its first
/// connection. The work itself is <see cref="PostLogUpload"/>, which Send logs now shares.
///
/// The registered Java name and the unique work name are persisted by WorkManager with
/// <c>ExistingPeriodicWorkPolicy.Keep</c>; moving either strands the schedule, so both are guarded.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.communication.LogUploadWorker")]
public class LogUploadWorker : Worker
{
    public const string WorkName = "log-upload";

    public LogUploadWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(LogUploadWorker), TimeSpan.FromDays(1))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public override Result DoWork() => PostDiagnostics.Worker(nameof(LogUploadWorker), Upload);

    private Result Upload()
    {
        if (!UploadWindow.Due(PostLogUpload.History(ApplicationContext).LastConfirmed, DateTimeOffset.Now))
        {
            return Result.InvokeSuccess();
        }
        // The worker bridges to the async upload at the true sync/async boundary, as EmailSyncWorker does.
        // A failed run is still a finished one: tomorrow's run is the retry (the outcome is recorded by
        // PostLogUpload), which keeps a phone with no network from waking its radio again and again.
        PostLogUpload.RunAsync(ApplicationContext, CancellationToken.None).GetAwaiter().GetResult();
        return Result.InvokeSuccess();
    }
}

/// <summary>Post's upload run is the shared head's, kept under this name for its callers.</summary>
internal static class PostLogUpload
{
    public static UploadHistory History(Context context) =>
        HeadDiagnostics.Current?.History ?? UploadHistory.None;

    /// <summary>The one sentence the Diagnostics screen shows about uploads, or null.</summary>
    public static string? Status(Context context) => HeadDiagnostics.Current?.UploadStatus;

    public static Task<LogUploadResult> RunAsync(Context context, CancellationToken cancellationToken) =>
        HeadDiagnostics.Current?.UploadAsync(cancellationToken)
        ?? Task.FromResult(new LogUploadResult(0, 0, null, "Post's diagnostics were not installed."));
}
