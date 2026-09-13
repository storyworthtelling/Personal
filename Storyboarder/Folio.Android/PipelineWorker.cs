using global::Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Folio.Domain.Identity;
using Folio.Infrastructure.Pipeline;
// AndroidX.Work also defines an IClock, so alias the domain one we mean.
using IClock = Folio.Domain.Time.IClock;

namespace Folio.Android;

// Drains the resumable pipeline a cycle at a time on WorkManager's schedule. Returns Retry when
// work remains (or a long backoff is pending) so WorkManager re-runs sooner than the 15-minute
// period; Success once the pipeline has nothing left to do. All progress is persisted to SQLite,
// so a novel advances across wakeups and survives reboots.
[global::Android.Runtime.Register(FolioIdentity.ApplicationId + ".PipelineWorker")]
public class PipelineWorker : Worker
{
    private const string WorkName = "storyboarder-pipeline";

    // WorkManager can run the periodic worker and a RunNow one-shot at the same time (they are separate
    // unique-work chains). Concurrent cycles double up on the same due work — and, when a call keeps
    // failing, on its retries. This process-wide gate lets only one cycle run at a time; a second
    // wakeup returns immediately and the already-running cycle drains the outstanding work.
    private static readonly SemaphoreSlim CycleGate = new(1, 1);

    public PipelineWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PipelineWorker>>();

        // Nothing the worker can do until the API keys are entered on the Settings page. Log it so a
        // conversion that never advances because it's unconfigured leaves a trace in the pipeline log.
        if (!SettingsStore.IsConfigured)
        {
            logger.LogInformation("Pipeline worker skipped: API keys are not configured yet.");
            return Result.InvokeSuccess();
        }

        if (!CycleGate.Wait(0))
        {
            logger.LogInformation("Pipeline cycle skipped: another cycle is already running.");
            return Result.InvokeSuccess();
        }

        try
        {
            var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            logger.LogInformation("Pipeline cycle starting.");

            var budget = new RunBudget(
                clock,
                (delay, token) => Task.Delay(delay, token),
                MaxIterations: 200,
                MaxDuration: TimeSpan.FromMinutes(9),     // stay under WorkManager's ~10-minute cap
                MaxInCycleWait: TimeSpan.FromSeconds(60));

            var outcome = runner.RunUntilExhaustedAsync(budget, CancellationToken.None)
                .GetAwaiter().GetResult();

            logger.LogInformation("Pipeline cycle finished: {Outcome}.", outcome);
            SettingsStore.LastRunOutcome = outcome.ToString();
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;
            return outcome == RunResult.Completed ? Result.InvokeSuccess() : Result.InvokeRetry();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline cycle threw and will be retried.");
            SettingsStore.LastRunOutcome = $"Failed: {ex.Message}";
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;
            return Result.InvokeRetry();
        }
        finally
        {
            CycleGate.Release();
        }
    }

    // The periodic backbone. 15 minutes is WorkManager's minimum period; Keep so app relaunches
    // don't reset a pending run.
    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(typeof(PipelineWorker), TimeSpan.FromMinutes(15))
            .SetConstraints(NetworkConnected())!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    // An immediate one-shot drain (used right after creating a project) so work starts without
    // waiting for the next periodic window.
    public static void RunNow(Context context)
    {
        var request = new OneTimeWorkRequest.Builder(typeof(PipelineWorker))
            .SetConstraints(NetworkConnected())!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniqueWork($"{WorkName}-now", ExistingWorkPolicy.Keep!, (OneTimeWorkRequest)request);
    }

    // Stops the periodic and one-shot pipeline work. Used before a backup restore swaps the database
    // and output files out from under a running cycle.
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
