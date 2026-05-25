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
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var delivery = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
            var today = DateOnly.FromDateTime(DateTime.Today);

            delivery.DeliverDailyPuzzle(today).GetAwaiter().GetResult();

            if (CrosswordDeliveryService.IsFirstSundayOfMonth(today))
                delivery.DeliverBonusPuzzle(today).GetAwaiter().GetResult();

            CredentialStore.LastDeliveryResult = $"Success — {today:MMM d, yyyy}";
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
            .AddTag(WorkName)
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName,
                ExistingPeriodicWorkPolicy.Keep,
                request);
    }
}
