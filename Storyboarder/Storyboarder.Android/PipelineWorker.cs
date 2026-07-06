using global::Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Storyboarder.Infrastructure.Pipeline;
// AndroidX.Work also defines an IClock, so alias the domain one we mean.
using IClock = Storyboarder.Domain.Time.IClock;

namespace Storyboarder.Android;

// Drains the resumable pipeline a cycle at a time on WorkManager's schedule. Returns Retry when
// work remains (or a long backoff is pending) so WorkManager re-runs sooner than the 15-minute
// period; Success once the pipeline has nothing left to do. All progress is persisted to SQLite,
// so a novel advances across wakeups and survives reboots.
[global::Android.Runtime.Register("com.storyworthtelling.storyboarder.PipelineWorker")]
public class PipelineWorker : Worker
{
    private const string WorkName = "storyboarder-pipeline";

    public PipelineWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        // Nothing the worker can do until the API keys are entered on the Settings page.
        if (!SettingsStore.IsConfigured)
            return Result.InvokeSuccess();

        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var budget = new RunBudget(
                clock,
                (delay, token) => Task.Delay(delay, token),
                MaxIterations: 200,
                MaxDuration: TimeSpan.FromMinutes(9),     // stay under WorkManager's ~10-minute cap
                MaxInCycleWait: TimeSpan.FromSeconds(60));

            var outcome = runner.RunUntilExhaustedAsync(budget, CancellationToken.None)
                .GetAwaiter().GetResult();

            SettingsStore.LastRunOutcome = outcome;
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;
            return outcome == RunResult.Completed ? Result.InvokeSuccess() : Result.InvokeRetry();
        }
        catch (Exception ex)
        {
            SettingsStore.LastRunOutcome = $"Failed: {ex.Message}";
            SettingsStore.LastRunAtUtc = DateTime.UtcNow;
            return Result.InvokeRetry();
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

    private static Constraints NetworkConnected() =>
        new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
}
