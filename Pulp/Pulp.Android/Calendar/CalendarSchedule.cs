using Android.Content;
using AndroidX.Work;
using Pulp.Domain;
using Pulp.Domain.Calendar;

namespace Pulp.Calendar;

/// <summary>
/// Puts the daily changes pass on WorkManager (D149): <see cref="CalendarSyncWorker"/> once a day, on a
/// connected network, the first run at the next 03:00 in the household's own zone
/// (<see cref="CalendarScreenSchedule"/>). It is unique work under the frozen
/// <see cref="PulpIdentity.DailyChangesWorkName"/> with <c>Keep</c>, so launching the app again never
/// resets a pending run and a phone that already has the row keeps exactly one.
/// </summary>
public static class CalendarSchedule
{
    public static void Schedule(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var delay = CalendarScreenSchedule.DelayUntilNextRun(DateTimeOffset.Now, TimeZoneInfo.Local);
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(CalendarSyncWorker), CalendarScreenSchedule.Repeat)
            .SetConstraints(constraints)!
            .SetInitialDelay((long)delay.TotalMinutes, Java.Util.Concurrent.TimeUnit.Minutes!)!
            .AddTag(PulpIdentity.DailyChangesWorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(PulpIdentity.DailyChangesWorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }
}
