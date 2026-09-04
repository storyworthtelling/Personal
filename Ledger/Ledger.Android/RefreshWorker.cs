using global::Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Ledger.Infrastructure.Pipeline;
// AndroidX.Work also defines an IClock, so alias the domain one we mean.
using IClock = Ledger.Domain.Time.IClock;

namespace Ledger.Android;

// Drains the resumable refresh pipeline a cycle at a time on WorkManager's schedule. Returns Retry
// when work remains (or a long backoff is pending) so WorkManager re-runs sooner than the daily
// period; Success once nothing is due. All progress is persisted to SQLite, so refreshes advance
// across wakeups and survive reboots.
[global::Android.Runtime.Register("com.storyworthtelling.ledger.RefreshWorker")]
public class RefreshWorker : Worker
{
    private const string WorkName = "ledger-refresh";

    // WorkManager can run the periodic worker and a RunNow one-shot at the same time (separate unique-
    // work chains). This process-wide gate lets only one cycle run at a time; a second wakeup returns
    // immediately and the already-running cycle drains the outstanding work.
    private static readonly SemaphoreSlim CycleGate = new(1, 1);

    public RefreshWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RefreshWorker>>();

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
