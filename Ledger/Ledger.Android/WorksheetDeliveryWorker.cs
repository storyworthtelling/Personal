using global::Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Index.Domain.Household;
using Index.Domain.Identity;
using Index.Infrastructure.Worksheet;

namespace Index.Android;

/// <summary>
/// D52/D89/D155: delivers the monthly budget worksheet "on its own" — no button anywhere in the app,
/// matching mockup 8's own copy ("the worksheet goes to the reMarkable on its own"). WorkManager has
/// no native monthly period, so this wakes on the same DAILY cadence <see cref="RefreshWorker"/> uses
/// and asks <see cref="WorksheetDeliveryService"/> what today holds: every wake runs the follow-up
/// cycle (D153) and the first read-back of a freshly delivered worksheet (the morning after it was
/// printed), and only on the day <c>WorksheetDeliverySchedule.IsDueToday</c> says is due does it compose
/// and deliver the next month's — a wakeup with none of those due is a fast, harmless no-op.
///
/// <para>The registered name and work name below join <see cref="RefreshWorker"/>'s own as frozen from
/// Index's first ship onward, guarded by the same reasoning
/// <c>Codex.Tests/Guard/FrozenInvariantsTests.cs</c>-style tests apply elsewhere: WorkManager
/// instantiates a periodic worker by this Java class name from a row persisted on the device, and a
/// moved name means the worksheet simply stops delivering, silently, with the phone still saying
/// "Refreshed" for its balances the whole time.</para>
/// </summary>
[global::Android.Runtime.Register(IndexIdentity.ApplicationId + ".WorksheetDeliveryWorker")]
public class WorksheetDeliveryWorker : Worker
{
    private const string WorkName = "index-worksheet-delivery";

    public WorksheetDeliveryWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WorksheetDeliveryWorker>>();

        if (!InstallRoleGate.MayRunPlaidWork(SettingsStore.CachedInstallRole ?? InstallRole.Owner))
        {
            logger.LogInformation("Worksheet delivery skipped: this install reads the household snapshot and never talks to Plaid.");
            return Result.InvokeSuccess();
        }

        if (!SettingsStore.IsConfigured)
        {
            // No Plaid credentials yet means no income/Snowball data to build a worksheet from.
            logger.LogInformation("Worksheet delivery skipped: Plaid credentials are not configured yet.");
            return Result.InvokeSuccess();
        }

        try
        {
            // The shared Claude key lives in Drive, read live (matching every other reader of it in
            // this app) — refreshed here, just before the extractor that needs it is ever built, the
            // same idiom MainPage/SetupPage already use before composing a voiced greeting. Best-effort:
            // a stale/empty cached key just means the read-back's own Claude call fails and is logged,
            // never that the whole worksheet cycle (composing and delivering NEXT month's page) is lost.
            try
            {
                scope.ServiceProvider.GetRequiredService<Storyworthtelling.Voice.DriveVoiceOptions>()
                    .RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refresh the shared Claude key before the worksheet cycle.");
            }

            var delivery = scope.ServiceProvider.GetRequiredService<WorksheetDeliveryService>();
            var result = delivery.RunCycleAsync(CancellationToken.None).GetAwaiter().GetResult();

            if (result.Ran)
            {
                logger.LogInformation("Worksheet cycle: {Summary}", result.Summarize());
                SettingsStore.LastWorksheetOutcome = result.Summarize();
                SettingsStore.LastWorksheetAtUtc = DateTime.UtcNow;
            }

            return Result.InvokeSuccess();
        }
        catch (Exception ex)
        {
            // Best-effort, like every other silent background pass in this app: a failed worksheet
            // cycle must not fail the daily balance refresh that runs alongside it, and the next
            // day's wakeup simply tries again (today's "IsDueToday" gate will already have passed by
            // then on the day it matters, since it only fires once a month).
            logger.LogError(ex, "Worksheet delivery cycle threw.");
            SettingsStore.LastWorksheetOutcome = $"Failed: {ex.Message}";
            SettingsStore.LastWorksheetAtUtc = DateTime.UtcNow;
            return Result.InvokeSuccess();
        }
    }

    // Daily, alongside RefreshWorker's own cadence — the internal IsDueToday gate is what makes this
    // effectively monthly without WorkManager needing a calendar-aware period it doesn't have.
    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(typeof(WorksheetDeliveryWorker), TimeSpan.FromHours(24))
            .SetConstraints(NetworkConnected())!
            .SetInitialDelay(2, Java.Util.Concurrent.TimeUnit.Hours!)!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public static void CancelAll(Context context) =>
        WorkManager.GetInstance(context).CancelUniqueWork(WorkName);

    private static Constraints NetworkConnected() =>
        new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
}
