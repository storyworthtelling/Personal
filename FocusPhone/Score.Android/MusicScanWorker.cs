using Codex.Ui;
using Android.Content;
using Android.Util;
using AndroidX.Work;
using Codex.Domain.Music;
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
///
/// A library the size of a real collection (thousands of files) can outlast a single
/// WorkManager execution window. Rather than race that limit, <see cref="ScanTimeBudget"/>
/// yields voluntarily first and reports <see cref="Result.InvokeRetry"/>, so WorkManager
/// runs the worker again on its own — no re-enqueue call needed, and no owner action
/// either. Each run resumes from exactly what <see cref="MusicScanner"/> already
/// committed, so a library too big for one pass still finishes on its own, in batches,
/// rather than needing to be retriggered by hand until it happens to get all the way
/// through.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.audio.MusicScanWorker")]
public class MusicScanWorker : Worker
{
    private const string Tag = "MusicScan";

    /// <summary>Unique-work name, so a second enqueue does not stack a duplicate scan.</summary>
    public const string WorkName = "music-library-scan";

    /// <summary>Unique-work name for the nightly automatic pass — deliberately its own unique
    /// work (a separate <see cref="PeriodicWorkRequest"/> from <see cref="Enqueue"/>'s
    /// on-demand <see cref="OneTimeWorkRequest"/>) so an owner-triggered "scan now" from
    /// Settings never collides with, or gets silently absorbed by, the nightly schedule.</summary>
    public const string NightlyWorkName = "music-library-nightly-scan";

    /// <summary>Progress keys, read back by <see cref="MusicLibrary"/>'s observer.</summary>
    public const string ProgressDone = "done";
    public const string ProgressTotal = "total";

    // Comfortably under the ~10-minute ceiling a plain (non-expedited, non-foreground)
    // WorkManager job can generally count on before the system reclaims it, leaving
    // room for the in-flight batch and, on the run that finally drains the backlog,
    // the dedup + tempo-backfill passes after it.
    private static readonly TimeSpan ScanTimeBudget = TimeSpan.FromMinutes(8);

    // Cancelled when WorkManager stops us (e.g. a replacing enqueue, or the system
    // reclaiming the execution window before our own budget check catches it).
    // MusicScanner checks the token once per batch, so this ends the scan at the next
    // batch boundary rather than tearing a half-written transaction.
    private readonly CancellationTokenSource cts = new();

    public MusicScanWorker(Context context, WorkerParameters parameters) : base(context, parameters)
    {
    }

    /// <summary>Network-constrained one-time work. Keep, so re-triggering while a scan
    /// is already running does not start a second pass over the same folder. Backoff is
    /// linear at the platform floor (10s) rather than the default exponential curve — a
    /// large library can need dozens of chained runs to fully drain, and exponential
    /// backoff would stretch that into hours.</summary>
    public static void Enqueue(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new OneTimeWorkRequest.Builder(typeof(MusicScanWorker))
            .SetConstraints(constraints)!
            .SetBackoffCriteria(BackoffPolicy.Linear!, TimeSpan.FromSeconds(10))!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniqueWork(WorkName, ExistingWorkPolicy.Keep!, (OneTimeWorkRequest)request);
    }

    /// <summary>
    /// The nightly automatic pass — the same worker and the same incremental plan as
    /// <see cref="Enqueue"/>, just triggered by the clock instead of the owner. Modeled on
    /// this head's own <c>FridayPicksWorker.Schedule</c> (a periodic WorkManager request
    /// enqueued unconditionally from <c>ScoreActivity.OnCreate</c> with
    /// <see cref="ExistingPeriodicWorkPolicy.Keep"/>, so a relaunch never disturbs work
    /// already scheduled) for the periodic-request shape, and on Foreword's
    /// <c>DeliveryWorker.Schedule</c> for aiming that request at a specific hour via
    /// <see cref="PeriodicWorkRequest.Builder.SetInitialDelay"/> — the delay itself is
    /// <see cref="NightlyRescanSchedule.DelayUntilNext"/>, pure and unit-tested. Whatever
    /// this run finds is recorded by <see cref="RescanOutcomeStore"/> for the owner's-voice
    /// greeting to read on the next app open (see <see cref="DoWork"/>).
    /// </summary>
    public static void ScheduleNightly(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var delay = NightlyRescanSchedule.DelayUntilNext(DateTimeOffset.Now);
        var request = (PeriodicWorkRequest)new PeriodicWorkRequest.Builder(
                typeof(MusicScanWorker), TimeSpan.FromHours(24))
            .SetConstraints(constraints)!
            .SetInitialDelay((long)delay.TotalMilliseconds, Java.Util.Concurrent.TimeUnit.Milliseconds!)!
            .AddTag(NightlyWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(NightlyWorkName, ExistingPeriodicWorkPolicy.Keep!, request);
    }

    public override void OnStopped()
    {
        cts.Cancel();
        base.OnStopped();
    }

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(MusicScanWorker), Work);


    private Result Work()

    {
        var folderId = MusicSettingsStore.MusicFolderId;
        if (string.IsNullOrEmpty(folderId))
        {
            return Result.InvokeSuccess();
        }

        try
        {
            var result = RunAsync(folderId).GetAwaiter().GetResult();

            // Recorded whether or not this run fully drained the backlog — the store
            // only PUBLISHES a finding once FullyDrained is true, but it still needs
            // every partial run's contribution folded into the running total to get
            // there (see RescanOutcomeStore).
            new RescanOutcomeStore(ApplicationContext!).RecordRun(result);
            AudioDiagnostics.LogInfo("Scan", result.Line());

            // A run that ran out of its time budget has already committed everything it
            // read so far; retrying resumes from exactly where the incremental plan left
            // off. Success here would tell MusicLibrary's observer — and the owner — the
            // scan finished when most of a 7,000-file library might still be unread.
            return result.FullyDrained ? Result.InvokeSuccess() : Result.InvokeRetry();
        }
        catch (OperationCanceledException)
        {
            // Stopped before our own budget check caught it (lost network, the system
            // reclaiming the window early). Retry, not Success, for the same reason as
            // above — this run may have made little or no progress.
            return Result.InvokeRetry();
        }
        catch (Exception e)
        {
            HeadLog.Warn(Tag, "Library scan failed", e);
            return Result.InvokeFailure();
        }
    }

    /// <returns>Whether this run fully drained the backlog, and what it found — see <see cref="MusicScanner.ScanAsync"/>.</returns>
    private async Task<MusicScanRunResult> RunAsync(string folderId)
    {
        var context = ApplicationContext!;
        var tokens = new DeviceAccountTokenProvider();
        var driveClient = new DriveMusicClient(tokens);
        var tagReader = new MusicTagReader(Path.Combine(context.CacheDir!.AbsolutePath, "art"));
        var scanner = new MusicScanner(driveClient, tokens, tagReader);

        MusicScanRunResult result;
        IReadOnlyList<Codex.Domain.Music.Track> catalog;
        using (var database = MusicDatabase.Open(context))
        {
            result = await scanner.ScanAsync(folderId, database.Catalog, (done, total) =>
            {
                SetProgressAsync(new Data.Builder()
                    .PutInt(ProgressDone, done)!
                    .PutInt(ProgressTotal, total)!
                    .Build()!);
            }, cts.Token, ScanTimeBudget);

            catalog = database.Catalog.LoadAll();
        }

        // Only once the backlog is actually drained — running this against a catalog
        // that's still missing most of a large library would waste the lookup on tracks
        // this same worker is about to re-read anyway on its next chained run.
        if (!result.FullyDrained)
        {
            return result;
        }

        // Tag BPM already ran during the scan above (MusicTagReader/TagMapping); this backfills
        // the long tail that never carries one via a free public catalogue (Deezer). Moved here
        // from the Workout screen's "Look up tempos" button, which ran the exact same live,
        // mid-session network lookup on demand — the owner asked for it to happen as part of
        // import instead, so a workout never has to wait on it. Best-effort: a network hiccup
        // here just leaves the long tail for the next scan, same as before.
        try
        {
            await new TempoBackfill(context).RunAsync(catalog, (_, _) => { }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"Tempo backfill failed: {e.Message}");
        }

        return result;
    }
}
