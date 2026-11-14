using Android.Content;
using AndroidX.Work;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain;
using Pulp.Domain.Core;
using Pulp.Domain.Diagnostics;

namespace Pulp.Catalogue;

/// <summary>
/// The weekly availability refresh (D204, audit W7.5, owner's ruling to build it), under the frozen
/// <see cref="PulpIdentity.WeeklyRefreshWorkerName"/>. It runs daily, so a week's tracked titles are spread
/// out, and looks again only at those a week stale, within the month's allowance
/// (<see cref="Pulp.Domain.Catalogue.WeeklyRefresh"/>). A lookup that fails keeps the cached copy; the run
/// itself only fails, to be retried, when the household cannot be read at all.
/// </summary>
[global::Android.Runtime.Register(PulpIdentity.WeeklyRefreshWorkerName)]
public class WeeklyRefreshWorker : Worker
{
    public WeeklyRefreshWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override void OnStopped() => HeadDiagnostics.Stopped(nameof(WeeklyRefreshWorker), StopReason);

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(WeeklyRefreshWorker), Work);

    private Result Work()
    {
        var services = PulpServices.For(ApplicationContext);
        try
        {
            var (refreshed, tracked, used) = services.RefreshTrackedAsync(CancellationToken.None).GetAwaiter().GetResult();
            HeadLog.Info("Catalogue", PulpLogLines.WeeklyRefresh(refreshed, tracked, used));
            return Result.InvokeSuccess()!;
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
        {
            services.Diagnostics.Warn("catalogue", "The weekly refresh could not read the household; WorkManager will try again.", exception);
            return Result.InvokeRetry()!;
        }
    }
}

/// <summary>Puts the weekly refresh on WorkManager: daily, on a connected network, unique under the frozen work name with <c>Keep</c>.</summary>
public static class WeeklyRefreshSchedule
{
    public static void Schedule(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(WeeklyRefreshWorker), TimeSpan.FromDays(1))
            .SetConstraints(constraints)!
            .AddTag(PulpIdentity.WeeklyRefreshWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(PulpIdentity.WeeklyRefreshWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }
}
