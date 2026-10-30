using Android.Content;
using Constitution.Body;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Household;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.Workouts;
using Constitution.Services;
using Constitution.WorkoutDelivery;
using Constitution.WorkoutWeek;

namespace Constitution.WeeklyMeals;

/// <summary>
/// The door from the head to the weekly meal run (part one): reads this install's own sources (the household's planning setup, the
/// clock), joins them into the run's inputs (<see cref="MealWeekJoin"/>, pure and tested), assembles the run's seams, runs
/// <see cref="MealWeekRun"/> and keeps the week for Today's dinner card and the Meals tab (<see cref="Source"/>).
///
/// <para><b>The calendar and the keys are the workout run's own.</b> The calendar read, the calendar writer, the record of written keys
/// and the record of issued numbers come from <see cref="WorkoutWeekCalendarSeams"/>, so a key means the same event to both runs and the meal
/// run finds an event the owner moved or deleted exactly the way the workout run does. The lease is <see cref="ConstitutionHousehold.Planning"/>.</para>
///
/// <para><b>Two runs share those records, so they take turns, and each sees the other's events.</b> The run goes through <see cref="RunGate.Shared"/>
/// (the workout run does too): both load the record of written keys, write the calendar and save it again, so two at once would lose keys. A run that
/// cannot get its turn within <see cref="RunGate.DefaultWait"/> does not start; <see cref="TryRunAsync"/> says so as a typed
/// <see cref="GateResult{T}.Busy"/>, and <see cref="RunAsync"/> throws <see cref="RunGateBusyException"/>. The calendars are read as
/// <see cref="KeyFamily.HouseholdMeal"/>: the run drops its own earlier cook blocks, slow-cooker starts and Groceries trip and keeps the workouts as
/// busy time, so a trip or a cook block is never put on a workout. Meals are placed first when both runs are due, and the engine places what is already
/// on the calendar first, so a workout run after this one goes around the cook blocks (it reads them as busy now).</para>
///
/// <para><b>Not yet here:</b> a worker to call <see cref="RunAsync"/> (a later job), a taste profile store (the source says there is none,
/// which plans without one), a source for the evenings the children eat at home, the Cook this soon mark, and an approval step. Nothing is swallowed
/// here: a seam's exception propagates to whoever calls. Device-gated: nothing here has met Google Calendar or a phone.</para>
/// </summary>
public static class MealWeekServices
{
    public const string FolderName = "meal-week";
    public const string HistoryFile = "cooked-history.json";
    public const string RecordFile = "planned-weeks.json";

    public static async Task<MealWeekOutcome> RunAsync(Context context, CancellationToken cancellation)
    {
        var turn = await TryRunAsync(context, cancellation);
        return turn switch
        {
            GateResult<MealWeekOutcome>.Ran ran => ran.Outcome,
            GateResult<MealWeekOutcome>.Busy busy => throw new RunGateBusyException(busy.Sentence),
            _ => throw new InvalidOperationException("The run gate gave an answer this door does not know."),
        };
    }

    /// <summary>The run, once it has its turn at the calendars; <see cref="GateResult{T}.Busy"/> when another placement run held them for the whole wait.</summary>
    public static Task<GateResult<MealWeekOutcome>> TryRunAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();
        return RunGate.Shared.RunAsync(token => RunAloneAsync(context, token), RunGate.DefaultWait, cancellation);
    }

    private static async Task<MealWeekOutcome> RunAloneAsync(Context context, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var appContext = context.ApplicationContext ?? context;
        var root = NoBackupRoot(appContext);
        var workScheduleJson = new ConstitutionConnections(appContext).WorkScheduleJson;
        var prepared = MealWeekJoin.Prepare(
            PlanningSetupFile.Read(Path.Combine(WorkoutWeekPaths.Folder(root), WorkoutWeekCalendarSeams.SetupFile)),
            string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson,
            TimeZoneInfo.Local,
            DateTimeOffset.Now);

        if (prepared is MealWeekPreparation.Stop stop)
        {
            // The run never started, so it wrote no line of its own.
            new BodyMealLog(new BodyLog(appContext)).Outcome(MealWeekTrail.LogPhrase(stop.Outcome));
            return stop.Outcome;
        }

        var ready = (MealWeekPreparation.Ready)prepared;
        var seams = Assemble(appContext, WorkoutWeekCalendarSeams.Create(appContext, KeyFamily.HouseholdMeal));
        return await MealWeekRun.RunAsync(ready.Inputs, seams, cancellation);
    }

    /// <summary>The saved weeks as the Meals tab and Today read them.</summary>
    public static IMealWeekSource Source(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new MealWeekRecordSource(new FileMealWeekRecords(Path.Combine(FolderOf(context.ApplicationContext ?? context), RecordFile)));
    }

    /// <summary>The run's seams. No taste profile is kept anywhere yet, so the source says there is none.</summary>
    public static MealWeekSeams Assemble(Context appContext, CalendarSideSeams calendar)
    {
        ArgumentNullException.ThrowIfNull(appContext);
        ArgumentNullException.ThrowIfNull(calendar);

        var household = ConstitutionHousehold.For(appContext);
        var folder = FolderOf(appContext);
        var log = new BodyMealLog(new BodyLog(appContext));
        return new MealWeekSeams(
            household.Planning.AskAsync,
            new HouseholdRecipeBox(household.Service),
            new MealHistoryStore(Path.Combine(folder, HistoryFile)),
            new NoMealTaste(),
            calendar.Calendars,
            new MealWithFallback(new NoMealAuthor(), new TrailMealLog(log)),
            calendar.CalendarWriter,
            calendar.WrittenKeys,
            calendar.KeyAssignments,
            new FileMealWeekRecords(Path.Combine(folder, RecordFile)),
            log);
    }

    private static string FolderOf(Context appContext) => Path.Combine(NoBackupRoot(appContext), FolderName);

    private static string NoBackupRoot(Context context) =>
        context.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the weekly meal run's records in.");

    // The Body trail takes the run's value-free phrases as they are.
    private sealed class BodyMealLog(BodyLog log) : IWorkoutWeekLog
    {
        public void Outcome(string phrase) => log.Outcome(phrase);
    }
}
