using Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Delivery;
using Foreword.Domain.Identity;
using Foreword.Infrastructure.Delivery;

namespace Foreword.Android;

// WorkManager persists the JAVA class name of a scheduled worker, and Schedule enqueues with
// ExistingPeriodicWorkPolicy.Keep — so a phone that already has work scheduled keeps a row
// naming this class. If the rename moved it, WorkManager would wake up, fail to instantiate a
// class that no longer exists, and the mornings would simply stop: no crash the owner sees, and
// no log they can reach after the cutover. So the registered name keeps "dailyloader" in it
// forever, even though everything around it now says Foreword. It is an address, not a
// description.
[global::Android.Runtime.Register(ForewordIdentity.ApplicationId + ".DeliveryWorker")]
public class DeliveryWorker : Worker
{
    public DeliveryWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }
    // Persisted WorkManager unique-work identity — deliberately kept as-is through the
    // "morning brief" rename; changing it would orphan the scheduled work or double-schedule.
    private const string WorkName = "crossword-delivery";

    // When the morning goes out, in local time. Public so Settings can SHOW the time the
    // schedule actually uses rather than a number typed into a label, which is how a screen
    // ends up confidently stating something the app does not do.
    public const int DeliveryHour = 6;
    public const int DeliveryMinute = 30;

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
        // Keep (not Update) so app re-launches don't reset the initial delay on a
        // pending-but-never-run job — that would starve a fresh install whose owner opens the app
        // after the morning window, pushing the first delivery to tomorrow again and again.
        //
        // What Keep also freezes is the TIME. A phone that has run an older build holds a row
        // scheduled for that build's hour, and moving the constant moves nothing: the schedule
        // goes on firing at the old time while Settings prints the new one. So the app remembers
        // the time the work was actually scheduled for, and replaces the work only when the two
        // disagree — which keeps Keep's protection and thaws what Keep froze.
        var wanted = DeliverySchedule.Stamp(DeliveryHour, DeliveryMinute);
        var replacing = DeliverySchedule.Decide(DeliveryHour, DeliveryMinute,
            CredentialStore.ScheduledDeliveryTime) == SchedulePolicy.Replace;

        var request = (PeriodicWorkRequest) new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(DeliveryWorker)),
                24,
                Java.Util.Concurrent.TimeUnit.Hours!)
            .SetInitialDelay(DelayUntilNext(DeliveryHour, DeliveryMinute), Java.Util.Concurrent.TimeUnit.Milliseconds!)
            .AddTag(WorkName)
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName,
                replacing
                    ? ExistingPeriodicWorkPolicy.CancelAndReenqueue
                    : ExistingPeriodicWorkPolicy.Keep,
                request);

        // Written after the enqueue, so a throw above leaves the stamp disagreeing and the next
        // launch tries again rather than recording a schedule that was never made.
        CredentialStore.ScheduledDeliveryTime = wanted;
    }

    // The phone's own clock, so the hour means the hour the owner reads on the Settings screen.
    // DeliverySchedule.DelayUntilNext carries the reasoning and the tests.
    private static long DelayUntilNext(int hour, int minute) =>
        (long) DeliverySchedule.DelayUntilNext(DateTimeOffset.Now, hour, minute).TotalMilliseconds;
}
