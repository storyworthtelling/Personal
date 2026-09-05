using Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using DailyLoader.Infrastructure.Delivery;

namespace DailyLoader.Android;

[global::Android.Runtime.Register("com.storyworthtelling.dailyloader.DeliveryWorker")]
public class DeliveryWorker : Worker
{
    public DeliveryWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }
    // Persisted WorkManager unique-work identity — deliberately kept as-is through the
    // "morning brief" rename; changing it would orphan the scheduled work or double-schedule.
    private const string WorkName = "crossword-delivery";

    public override Result DoWork()
    {
        if (!CredentialStore.IsConfigured)
            return Result.InvokeSuccess();

        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var delivery = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
            var today = DateOnly.FromDateTime(DateTime.Today);

            var newsStatus = delivery.DeliverDailyPuzzle(today).GetAwaiter().GetResult();

            if (CrosswordDeliveryService.IsFirstSundayOfMonth(today))
                delivery.DeliverBonusPuzzle(today).GetAwaiter().GetResult();

            if (CredentialStore.IsGoogleConfigured)
            {
                var daily = scope.ServiceProvider.GetRequiredService<DailyDeliveryService>();
                daily.EnsureKidCalendars().GetAwaiter().GetResult();
                daily.DeliverDailyDocuments(today).GetAwaiter().GetResult();
            }

            CredentialStore.LastDeliveryOutcome = $"Delivered ({newsStatus})";
            CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            return Result.InvokeSuccess();
        }
        catch (Exception ex)
        {
            CredentialStore.LastDeliveryOutcome = $"Failed: {ex.Message}";
            CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            return Result.InvokeRetry();
        }
    }

    public static void Schedule(Context context)
    {
        var request = (PeriodicWorkRequest) new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(DeliveryWorker)),
                24,
                Java.Util.Concurrent.TimeUnit.Hours!)
            .SetInitialDelay(DelayUntilNext(hour: 10, minute: 45), Java.Util.Concurrent.TimeUnit.Milliseconds!)
            .AddTag(WorkName)
            .Build();

        // Keep (not Update) so app re-launches don't reset the initial delay on a
        // pending-but-never-run job. Update only helps after the first run (when
        // WorkManager uses periodStartTime, not initialDelay), so it buys nothing
        // for drift correction while breaking fresh installs where the user opens
        // the app after the delivery window.
        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName,
                ExistingPeriodicWorkPolicy.Keep,
                request);
    }

    private static long DelayUntilNext(int hour, int minute)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, TimeSpan.Zero);
        if (target <= now)
            target = target.AddDays(1);
        return (long)(target - now).TotalMilliseconds;
    }
}
