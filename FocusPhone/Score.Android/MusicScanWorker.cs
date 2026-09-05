using Android.Content;
using Android.Util;
using AndroidX.Work;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Storage;
using Score.Google;

namespace Score;

/// <summary>
/// Runs the Drive library scan under WorkManager rather than as a fire-and-forget
/// Task on the Activity, so it is not tied to the Activity's lifecycle: once enqueued
/// it keeps running even after Score is backgrounded, instead of dying whenever
/// Android decides to reclaim a backgrounded process mid-scan. It builds its own
/// dependencies from the application context — nothing here may capture the Activity,
/// which is the whole point.
///
/// It reports (done, total) through <see cref="ListenableWorker.SetProgressAsync"/>,
/// which <see cref="MusicLibrary"/> observes while its screen is up; when nothing is
/// observing (the app is away), the scan still finishes and writes the catalog, and
/// the next <see cref="MusicLibrary.LoadCatalog"/> on resume picks it up.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.audio.MusicScanWorker")]
public class MusicScanWorker : Worker
{
    private const string Tag = "MusicScan";

    /// <summary>Unique-work name, so a second enqueue does not stack a duplicate scan.</summary>
    public const string WorkName = "music-library-scan";

    /// <summary>Progress keys, read back by <see cref="MusicLibrary"/>'s observer.</summary>
    public const string ProgressDone = "done";
    public const string ProgressTotal = "total";

    // Cancelled when WorkManager stops us (e.g. a replacing enqueue). MusicScanner
    // checks the token once per batch, so this ends the scan at the next batch
    // boundary rather than tearing a half-written transaction.
    private readonly CancellationTokenSource cts = new();

    public MusicScanWorker(Context context, WorkerParameters parameters) : base(context, parameters)
    {
    }

    /// <summary>Network-constrained one-time work. Keep, so re-triggering while a scan
    /// is already running does not start a second pass over the same folder.</summary>
    public static void Enqueue(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new OneTimeWorkRequest.Builder(typeof(MusicScanWorker))
            .SetConstraints(constraints)!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniqueWork(WorkName, ExistingWorkPolicy.Keep!, (OneTimeWorkRequest)request);
    }

    public override void OnStopped()
    {
        cts.Cancel();
        base.OnStopped();
    }

    public override Result DoWork()
    {
        var folderId = MusicSettingsStore.MusicFolderId;
        if (string.IsNullOrEmpty(folderId))
        {
            return Result.InvokeSuccess();
        }

        try
        {
            RunAsync(folderId).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch (OperationCanceledException)
        {
            // Stopped by WorkManager — a partial scan is fine; the plan is incremental,
            // so the next enqueue resumes from what the cache already knows.
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"Library scan failed: {e.Message}");
            return Result.InvokeFailure();
        }
    }

    private async Task RunAsync(string folderId)
    {
        var context = ApplicationContext!;
        var tokens = new DeviceAccountTokenProvider();
        var driveClient = new DriveMusicClient(tokens);
        var tagReader = new MusicTagReader(Path.Combine(context.CacheDir!.AbsolutePath, "art"));
        var scanner = new MusicScanner(driveClient, tokens, tagReader);

        using var database = MusicDatabase.Open(context);
        await scanner.ScanAsync(folderId, database.Catalog, (done, total) =>
        {
            SetProgressAsync(new Data.Builder()
                .PutInt(ProgressDone, done)!
                .PutInt(ProgressTotal, total)!
                .Build()!);
        }, cts.Token);
    }
}
