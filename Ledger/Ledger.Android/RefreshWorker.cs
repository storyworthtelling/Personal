using global::Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Index.Domain.Household;
using Index.Domain.Identity;
using Index.Infrastructure.Household;
using Index.Infrastructure.Pipeline;
// AndroidX.Work also defines an IClock, so alias the domain one we mean.
using IClock = Index.Domain.Time.IClock;

namespace Index.Android;

// Drains the resumable refresh pipeline a cycle at a time on WorkManager's schedule. Returns Retry
// when work remains (or a long backoff is pending) so WorkManager re-runs sooner than the daily
// period; Success once nothing is due. All progress is persisted to SQLite, so refreshes advance
// across wakeups and survive reboots.
//
// The registered name and WorkName below are the old Ledger's, kept as they were, following the
// Foreword/Folio pattern: WorkManager instantiates the worker by this Java class name from a row
// persisted on the device, and a moved name means the row names a class that no longer exists —
// the refresh simply stops, silently. A phone upgrading from Ledger already has that row; a new
// WorkName would leave it running beside a second schedule.
[global::Android.Runtime.Register(IndexIdentity.ApplicationId + ".RefreshWorker")]
public class RefreshWorker : Worker
{
    private const string WorkName = "ledger-refresh";

    // WorkManager can run the periodic worker and a RunNow one-shot at the same time (separate unique-
    // work chains). This process-wide gate lets only one cycle run at a time; a second wakeup returns
    // immediately and the already-running cycle drains the outstanding work.
    private static readonly SemaphoreSlim CycleGate = new(1, 1);

    /// <summary>Whether a refresh cycle is running in this process right now, read off the same gate that
    /// refuses a second one. Accounts asks it (refresh on open, D149) rather than keeping a flag of its
    /// own that could disagree with the worker's.</summary>
    public static bool IsCycleRunning => CycleGate.CurrentCount == 0;

    public RefreshWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RefreshWorker>>();

        // Her install never talks to Plaid. Launch cancels this work when the role settles as Hers, but a
        // cycle already queued from before the role was known must not run either.
        if (!InstallRoleGate.MayRunPlaidWork(SettingsStore.CachedInstallRole ?? InstallRole.Owner))
        {
            logger.LogInformation("Refresh worker skipped: this install reads the household snapshot and never talks to Plaid.");
            return Result.InvokeSuccess();
        }

        // Nothing to do until the Plaid credentials are entered on the Settings page.
        if (!SettingsStore.IsConfigured)
        {
            logger.LogInformation("Refresh worker skipped: Plaid credentials are not configured yet.");
            return Result.InvokeSuccess();
        }

        if (!CycleGate.Wait(0))
        {
            logger.LogInformation("Refresh cycle skipped: another cycle is already running.");
            return Result.InvokeSuccess();
        }

        try
        {
            var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            logger.LogInformation("Refresh cycle starting.");

            var budget = new RunBudget(
                clock,
                (delay, token) => Task.Delay(delay, token),
                MaxIterations: 100,
                MaxDuration: TimeSpan.FromMinutes(9),     // stay under WorkManager's ~10-minute cap
                MaxInCycleWait: TimeSpan.FromSeconds(60));

            var outcome = runner.RunUntilExhaustedAsync(budget, CancellationToken.None)
                .GetAwaiter().GetResult();

            logger.LogInformation("Refresh cycle finished: {Outcome}.", outcome);
            SettingsStore.LastRunOutcome = outcome == RunResult.Completed ? "Refreshed" : outcome.ToString();
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;

            // Release I (D10): publish the household snapshot after every successful cycle, the same
            // "best-effort, never block on failure" idiom VaultSync.PublishAsync already follows after
            // a settings save/connect/remove — her install's next read is simply as stale as the last
            // successful publish, never a reason to fail the refresh cycle itself.
            if (outcome == RunResult.Completed)
            {
                PublishHouseholdSnapshot(scope, logger);
            }

            return outcome == RunResult.Completed ? Result.InvokeSuccess() : Result.InvokeRetry();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refresh cycle threw and will be retried.");
            SettingsStore.LastRunOutcome = $"Failed: {ex.Message}";
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;
            return Result.InvokeRetry();
        }
        finally
        {
            CycleGate.Release();
        }
    }

    // Best-effort: no passphrase or no signed-in account yet is an ordinary, quiet skip
    // (HouseholdSnapshotPublisher.PublishAsync's own precondition), and any other failure (Drive
    // unreachable, etc.) is logged and swallowed — the same "never let a remote failure block a local
    // state change" idiom the rest of this worker and SetupPage.CommitPendingUndo already follow.
    private static void PublishHouseholdSnapshot(IServiceScope scope, ILogger logger)
    {
        try
        {
            var publisher = scope.ServiceProvider.GetRequiredService<HouseholdSnapshotPublisher>();
            var result = publisher.PublishAsync(
                    SettingsStore.VaultPassphrase, SettingsStore.GoogleAccountEmail, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!result.Ok)
            {
                logger.LogWarning("Household snapshot publish failed: {Message}", result.Message);
            }
            else if (result.Message is not null)
            {
                logger.LogInformation("Household snapshot not published: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Household snapshot publish threw and was skipped.");
        }
    }

    // The periodic backbone: once a day, on a connected network. Keep so app relaunches don't reset a
    // pending run. Balances change slowly, so a daily cadence is plenty and keeps Plaid calls minimal.
    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(typeof(RefreshWorker), TimeSpan.FromHours(24))
            .SetConstraints(NetworkConnected())!
            .SetInitialDelay(1, Java.Util.Concurrent.TimeUnit.Hours!)!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    // An immediate one-shot drain (used by "Refresh now" and right after connecting an institution) so
    // balances update without waiting for the next daily window.
    public static void RunNow(Context context)
    {
        var request = new OneTimeWorkRequest.Builder(typeof(RefreshWorker))
            .SetConstraints(NetworkConnected())!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniqueWork($"{WorkName}-now", ExistingWorkPolicy.Keep!, (OneTimeWorkRequest)request);
    }

    public static void CancelAll(Context context)
    {
        var manager = WorkManager.GetInstance(context);
        manager.CancelUniqueWork(WorkName);
        manager.CancelUniqueWork($"{WorkName}-now");
    }

    private static Constraints NetworkConnected() =>
        new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
}
