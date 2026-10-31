using Android.Content;
using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;
using Constitution.Domain.Today;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.ReadBack;
using Constitution.Services;
using Constitution.WorkoutDelivery;
using Constitution.WorkoutWeek;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The door from the head to what the weekly run saved: the Workouts tab's source and its calendar follow, and the Today clipping. The week is
/// read from the run's own report and the number records it keeps, so the tab names events by the keys the run wrote; a swap or a skip made on
/// the tab goes into the owner's moves the run reads, the skip ledger the read-back keeps, and the calendar. Every file is in the app's
/// no-backup folder, and no permission is asked for here.
///
/// <para>Device-gated: nothing here has met a phone, a calendar or a real saved week.</para>
/// </summary>
public static class WorkoutsTabServices
{
    public static void Wire(Context context, WorkoutsArea area)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(area);

        var appContext = context.ApplicationContext ?? context;
        var root = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the weekly run's records in.");
        var folder = WorkoutWeekPaths.Folder(root);
        var calendarSide = WorkoutWeekCalendarSeams.Create(appContext);
        var owner = calendarSide.Standing.Owner;
        var delivered = new FileDeliveredWorkouts(WorkoutWeekPaths.Delivered(root));
        var ledger = ReadBackServices.LedgerOf(appContext);

        var real = new ReportWorkoutsSource(
            new FileWorkoutWeekReports(Path.Combine(folder, WorkoutWeekFiles.ReportFile)),
            calendarSide.KeyAssignments,
            ledger,
            new CalendarWeekRequests(appContext, folder, calendarSide.Calendars, ledger),
            day => owner is not null && delivered.Find(DeliveredName.For(DeliveryKind.Workout, day, owner)) is not null,
            owner);
        var overrides = new FileWorkoutOverrides(Path.Combine(folder, WorkoutWeekFiles.OverridesFile));

        area.Source = new OverridingWorkoutsSource(new SettlingWorkoutsSource(real, new LedgerSkipSettler(ReadBackServices.LedgerOf(context))), overrides);
        area.Calendar = new WorkoutCalendarFollow(calendarSide.CalendarWriter, calendarSide.EventEditor, calendarSide.WrittenKeys, calendarSide.KeyAssignments);
    }

    /// <summary>Today's plan from the saved week, or null (no report, an unreadable one, a rest day, or no owner marked). Read on the owner's clock.</summary>
    public static TodayPlan? TodayOf(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var appContext = context.ApplicationContext ?? context;
        var root = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the weekly run's records in.");
        var folder = WorkoutWeekPaths.Folder(root);
        var workScheduleJson = new ConstitutionConnections(appContext).WorkScheduleJson;
        var setup = PlanningSetupFile.Read(Path.Combine(folder, WorkoutWeekCalendarSeams.SetupFile));
        var standing = CalendarSideStanding.Decide(Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn, setup, string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson, TimeZoneInfo.Local);
        if (standing.Owner is not { } owner)
        {
            return null;
        }

        var zone = setup is HouseholdSetupRead.Configured configured
            ? SetupRules.From(configured.Setup, string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson, TimeZoneInfo.Local).Rules.Zone
            : TimeZoneInfo.Local;
        var record = new FileWorkoutWeekReports(Path.Combine(folder, WorkoutWeekFiles.ReportFile)).Load();
        return TodayPlanFromRecord.For(record, owner, DateTimeOffset.Now, zone);
    }
}
