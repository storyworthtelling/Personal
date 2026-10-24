using Android.Content;
using Constitution.Body;
using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using Constitution.Services;
using Constitution.Settings;
using Constitution.WorkoutDelivery;

namespace Constitution.WorkoutWeek;

/// <summary>
/// The door from the head to the weekly workout run: reads this install's own sources (the household's planning setup, the equipment
/// checklist, the owner's moves, the clock), joins them into the run's request (<see cref="WorkoutWeekJoin"/>, pure and tested),
/// assembles the run's seams from the calendar side and the delivery side, runs <see cref="WorkoutWeekRun"/>, sends again whatever the
/// tablet refused earlier, and keeps the report the Workouts tab and Today read (<see cref="IWorkoutWeekReports"/>).
///
/// <para><b>Nothing is swallowed here.</b> A week that cannot be planned comes back as the typed
/// <see cref="WorkoutWeekOutcome.NotPlanned"/> with its specific reason (no household setup yet; calendars unreadable; records unreadable;
/// no adult), and an exception from a seam or a file propagates to the worker, which logs its type name and nothing else. A run that did
/// not plan leaves the last report where it is. The run puts its own value-free line in the Body trail when it finishes, so this
/// door adds one only for the week it never started.</para>
///
/// <para>The signature of <see cref="RunAsync"/> is the contract the worker calls. Device-gated: nothing here has met Google Calendar,
/// the reMarkable cloud or a phone.</para>
/// </summary>
public static class WorkoutWeekServices
{
    public static async Task<WorkoutWeekOutcome> RunAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();

        var appContext = context.ApplicationContext ?? context;
        var folder = WorkoutWeekPaths.Folder(
            appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the weekly run's records in."));
        var workScheduleJson = new ConstitutionConnections(appContext).WorkScheduleJson;

        var prepared = WorkoutWeekJoin.Prepare(
            PlanningSetupFile.Read(Path.Combine(folder, WorkoutWeekCalendarSeams.SetupFile)),
            string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson,
            TimeZoneInfo.Local,
            new EquipmentStore(appContext).Read(),
            new FileWorkoutOverrides(Path.Combine(folder, WorkoutWeekFiles.OverridesFile)).Load(),
            DateTimeOffset.Now);

        if (prepared is WorkoutWeekPreparation.Stop stop)
        {
            // The run never started, so it wrote no line of its own.
            new BodyLog(appContext).Outcome(WorkoutWeekTrail.LogPhrase(stop.Outcome));
            return stop.Outcome;
        }

        var ready = (WorkoutWeekPreparation.Ready)prepared;
        var calendarSide = WorkoutWeekCalendarSeams.Create(appContext);
        var deliverySide = WorkoutWeekDeliverySeams.Create(appContext);
        var seams = Assemble(calendarSide, deliverySide);

        var outcome = await new WorkoutWeekRun(seams).RunAsync(ready.Request, cancellation);

        // Whatever the tablet refused earlier goes again, now that the run has had its chance to fix the pairing's notice.
        await deliverySide.Delivery.RetryPending();

        if (outcome is WorkoutWeekOutcome.Planned planned)
        {
            var issued = calendarSide.KeyAssignments.Load();
            var today = ZoneClock.DayOf(ready.Request.Now, ready.Request.Rules.Zone);
            var record = WorkoutWeekRecordBuilder.From(
                planned.Report, issued.IsReadable ? issued.Assignments : Constitution.Domain.Placement.KeyAssignments.Empty, ready.Request.Now, today);
            new FileWorkoutWeekReports(Path.Combine(folder, WorkoutWeekFiles.ReportFile)).Save(record);
        }

        return outcome;
    }

    /// <summary>The run's seams, a straight copy of the two sides. No forecast or hotel client exists yet; the calendar side says so honestly and the run reads it as "changes nothing".</summary>
    public static WorkoutWeekSeams Assemble(CalendarSideSeams calendar, DeliverySideSeams delivery)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(delivery);

        return new WorkoutWeekSeams(
            calendar.Calendars,
            calendar.Learning,
            new PlanWithFallback(new NoPlanAuthor(), delivery.PlanLog),
            calendar.Conditions,
            calendar.HotelGyms,
            calendar.Recovery,
            delivery.Encouragement,
            calendar.CalendarWriter,
            calendar.WrittenKeys,
            calendar.KeyAssignments,
            delivery.Delivery,
            delivery.Tablet,
            delivery.InkProbe,
            delivery.Expected,
            delivery.Delivered,
            delivery.Log);
    }
}
