using Android.Content;
using AndroidX.Work;
using Pulp.App;
using Pulp.Domain;
using Pulp.Domain.Calendar;
using Pulp.Infrastructure.Calendar;

namespace Pulp.Calendar;

/// <summary>
/// What the composition hands the worker (set once at launch, in <c>PulpServices</c>): the feed that reads
/// the household's items, the Google calendar, and where a failure is written. Null until composed, in
/// which case the worker has nothing to do and says so by succeeding.
/// </summary>
public static class CalendarSyncHooks
{
    public static ICalendarFeed? Feed { get; set; }

    public static IPulpCalendar? Calendar { get; set; }

    public static Action<string, Exception?>? Warn { get; set; }
}

/// <summary>
/// The nightly calendar write (D149). It rides the <b>daily changes pass</b>: the frozen
/// <see cref="PulpIdentity.DailyChangesWorkerName"/> and <see cref="PulpIdentity.DailyChangesWorkName"/>
/// already exist for it, so no new persisted name is added. Any later step of that pass belongs in this
/// worker's <see cref="DoWork"/>; WorkManager keeps one row for the class name. All decisions are in
/// <see cref="CalendarNightly"/>; this only runs it and maps the failure to a WorkManager result.
/// </summary>
[global::Android.Runtime.Register(PulpIdentity.DailyChangesWorkerName)]
public class CalendarSyncWorker : Worker
{
    public CalendarSyncWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        // WorkManager can start the process with no shell, so the hooks are still unset; composing the
        // services is what sets them. Without this the worker would find them null and report success
        // having written nothing.
        PulpServices.For(ApplicationContext);

        var feed = CalendarSyncHooks.Feed;
        var calendar = CalendarSyncHooks.Calendar;
        if (feed is null || calendar is null)
        {
            return Result.InvokeSuccess()!;
        }

        try
        {
            CalendarNightly.RunAsync(feed, calendar, DateOnly.FromDateTime(DateTime.Now), CancellationToken.None).GetAwaiter().GetResult();
            return Result.InvokeSuccess()!;
        }
        catch (Exception exception)
        {
            var advice = CalendarFailures.Advise(exception);
            if (advice == CalendarAdvice.QuietRetry)
            {
                return Result.InvokeRetry()!;
            }

            // One constant sentence per verdict; each part goes alongside it, so the hook can read what Google said.
            var message = advice == CalendarAdvice.Retry
                ? "The Pulp calendar could not be written tonight; WorkManager will try again."
                : "The Pulp calendar write failed and will not mend by retrying tonight.";
            foreach (var part in CalendarFailures.Parts(exception))
            {
                CalendarSyncHooks.Warn?.Invoke(message, part);
            }

            return advice == CalendarAdvice.Retry ? Result.InvokeRetry()! : Result.InvokeFailure()!;
        }
    }
}
