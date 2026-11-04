using Storyworthtelling.Maui.Diagnostics;
using Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Calendar;
using Foreword.Domain.Delivery;
using Foreword.Domain.Identity;
using Foreword.Infrastructure.Delivery;
using Foreword.Infrastructure.Prayer;

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

    private bool retrying;

    public override Result DoWork() => MauiLogLoop.Worker(nameof(DeliveryWorker), () =>
    {
        retrying = false;
        var result = Work();
        // Queue tomorrow from the wall clock now, unless this run is going round again. Doing it
        // here rather than relying on a periodic request is the fix for mornings drifting to the
        // afternoon: a periodic request re-anchors to when each run finished.
        if (!retrying)
            Enqueue(ApplicationContext, ExistingWorkPolicy.AppendOrReplace);
        UploadLogs();
        return result;
    });

    // The log loop's upload rides this worker (LOG-LOOP-PLAN.md §3.3): it acts only inside the Saturday window,
    // until a run is confirmed, and never fails the worker's own work.
    private static void UploadLogs()
    {
        try
        {
            MauiLogLoop.UploadIfDueAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            MauiLog.Current?.Write("LogUpload", Microsoft.Extensions.Logging.LogLevel.Warning, "The log upload threw.", e);
        }
    }

    private Result Work()
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
                daily.EnsureColumnCalendars().GetAwaiter().GetResult();
                var unreadable = daily.DeliverDailyDocuments(today).GetAwaiter().GetResult();
                CredentialStore.UnreadableCalendars = UnreadableCalendarNotice.Store(unreadable);

                // The third document, in its own try. Prayer is the one part of the morning that
                // must not be able to take the other two down with it: it reads a file the other
                // two do not, behind a passphrase the other two do not need, so its failures are
                // its own — and a morning with no prayer sheet is a smaller loss than a morning
                // with no day and no paper.
                try
                {
                    scope.ServiceProvider.GetRequiredService<PrayerDeliveryService>()
                        .Deliver(today).GetAwaiter().GetResult();
                    CredentialStore.LastPrayerOutcome = "Delivered";
                }
                catch (Exception ex)
                {
                    CredentialStore.LastPrayerOutcome = $"Failed: {ex.Message}";
                }
            }

            CredentialStore.LastDeliveryOutcome = $"Delivered ({newsStatus})";
            CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            return Result.InvokeSuccess();
        }
        catch (Exception ex)
        {
            CredentialStore.LastDeliveryOutcome = $"Failed: {ex.Message}";
            CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            // Bounded: tomorrow is queued behind this run, so it cannot retry forever.
            if (!DeliverySchedule.ShouldRetry(RunAttemptCount))
                return Result.InvokeSuccess();
            retrying = true;
            return Result.InvokeRetry();
        }
    }

    public static void Schedule(Context context)
    {
        // Keep so app re-launches don't reset the initial delay on a
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

        if (replacing)
        {
            // The old periodic request lives under the same unique name; cancel it so it cannot
            // go on firing at its drifted hour beside the chain.
            WorkManager.GetInstance(context).CancelUniqueWork(WorkName);
        }

        Enqueue(context, replacing ? ExistingWorkPolicy.Replace : ExistingWorkPolicy.Keep);

        // Written after the enqueue, so a throw above leaves the stamp disagreeing and the next
        // launch tries again rather than recording a schedule that was never made.
        CredentialStore.ScheduledDeliveryTime = wanted;
    }

    // One-time work aimed at the next 6:30 on the phone's clock. Each run queues the next, so the
    // hour is recomputed every day instead of inherited from whenever the last run finished.
    private static void Enqueue(Context context, ExistingWorkPolicy policy)
    {
        var request = (OneTimeWorkRequest) new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(DeliveryWorker)))
            .SetInitialDelay(DelayUntilNext(DeliveryHour, DeliveryMinute), Java.Util.Concurrent.TimeUnit.Milliseconds!)
            .AddTag(WorkName)
            .Build();

        WorkManager.GetInstance(context).EnqueueUniqueWork(WorkName, policy, request);
    }

    // The phone's own clock, so the hour means the hour the owner reads on the Settings screen.
    // DeliverySchedule.DelayUntilNext carries the reasoning and the tests.
    private static long DelayUntilNext(int hour, int minute) =>
        (long) DeliverySchedule.DelayUntilNext(DateTimeOffset.Now, hour, minute).TotalMilliseconds;
}
