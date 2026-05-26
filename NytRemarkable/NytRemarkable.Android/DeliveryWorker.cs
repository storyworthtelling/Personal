using Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using NytRemarkable.Infrastructure.Delivery;

namespace NytRemarkable.Android;

[global::Android.Runtime.Register("com.storyworthtelling.nytremarkable.DeliveryWorker")]
public class DeliveryWorker : Worker
{
    public DeliveryWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }
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

            CredentialStore.LastDeliveryResult = $"Success — {today:MMM d, yyyy} ({newsStatus})";
            return Result.InvokeSuccess();
        }
        catch (Exception ex)
        {
            CredentialStore.LastDeliveryResult = $"Failed — {DateTime.Today:MMM d, yyyy}: {ex.Message}";
            return Result.InvokeRetry();
        }
    }

    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(DeliveryWorker)),
                24,
                Java.Util.Concurrent.TimeUnit.Hours!)
            .SetInitialDelay(DelayUntilNext(hour: 10, minute: 45), Java.Util.Concurrent.TimeUnit.Milliseconds!)
            .AddTag(WorkName)
            .Build();

        // Update (not Keep) so re-launches recalculate the initial delay
        // if the schedule has drifted.
        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName,
                ExistingPeriodicWorkPolicy.Update,
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
