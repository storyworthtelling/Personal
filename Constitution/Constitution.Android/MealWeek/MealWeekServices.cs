using Android.Content;
using Constitution.Body;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Household;
using Constitution.Infrastructure.Budgets;
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
/// <para><b>The paper (part two).</b> After the week is kept, the same run turns it into the meal sheet and each cook's recipe and puts them on this phone's tablet
/// (<see cref="MealDocumentsStep"/>); the tablet, the delivery service, the ink probe and the read-back's record are the workout run's own
/// (<see cref="WorkoutWeekDeliverySeams"/>). A recipe goes an hour before its cook block, so it waits for a later call: there is no scheduled one yet.</para>
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
    public const string DeliveredFile = "delivered-documents.json";
    public const string DecisionFile = "decided-weeks.json";

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

    /// <summary>
    /// The run's seams. No taste profile is kept anywhere yet, so the source says there is none. The documents step reuses the workout run's delivery side
    /// (<see cref="WorkoutWeekDeliverySeams"/>: the tablet client bound to this phone's pairing, the delivery service, the ink probe and the read-back's own
    /// record of expected documents), so a title means the same page to both, plus the meal run's own record of what it has sent. The holiday calendar is the
    /// default that cannot be asked, so the sheet carries no ask-ahead page until a calendar reader exists. The rotating templates read and keep their library in the
    /// household file (<see cref="HouseholdTemplateLibrary"/>); the Claude author (<see cref="TemplateAuthorServices"/>) proposes new lines when a key is set; with no key nothing is proposed and pages 2 and 3 are
    /// printed only from templates already approved in the file.
    /// </summary>
    public static MealWeekSeams Assemble(Context appContext, CalendarSideSeams calendar)
    {
        ArgumentNullException.ThrowIfNull(appContext);
        ArgumentNullException.ThrowIfNull(calendar);

        var household = ConstitutionHousehold.For(appContext);
        var folder = FolderOf(appContext);
        var log = new BodyMealLog(new BodyLog(appContext));
        var delivery = WorkoutWeekDeliverySeams.Create(appContext);
        var documents = new MealDocumentSeams(
            delivery.Delivery,
            delivery.Tablet,
            delivery.InkProbe,
            delivery.Expected,
            new FileDeliveredMealDocuments(Path.Combine(folder, DeliveredFile)),
            new HouseholdMealRoster(household.Service),
            new HouseholdMealPantry(household.Service),
            new FileMonthlyBudgets(Path.Combine(NoBackupRoot(appContext), FileMonthlyBudgets.FileName)),
            new UnavailableHolidayCalendar(),
            log);
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
            log,
            documents,
            new MealTemplateSeams(new HouseholdTemplateLibrary(household.Service), TemplateAuthorServices.AuthorOf(appContext)));
    }

    /// <summary>
    /// The seams of the deadline step (<see cref="MealDeadlineStep"/>), the part of the meal week that runs once the sheet's deadline has passed. The lease, the kept
    /// weeks, the household file's answers, library and taste notes are this install's own; the record of decided weeks is a small file beside the kept weeks; the calendar side (the meal key family's writer, the narrow editor and the written keys the meal run placed with) takes a denied evening's cook block off (D111). <b>The taste
    /// judge is <see cref="UnavailableTasteJudge"/>:</b> no Claude judge exists yet, so written words are recorded as unjudged and asked again on the next run. Nothing calls
    /// the step yet (a worker is a later job), and nothing here holds a secret.
    /// </summary>
    public static MealDeadlineSeams AssembleDeadline(Context appContext)
    {
        ArgumentNullException.ThrowIfNull(appContext);

        var household = ConstitutionHousehold.For(appContext);
        var folder = FolderOf(appContext);
        var calendar = WorkoutWeekCalendarSeams.Create(appContext, KeyFamily.HouseholdMeal);
        return new MealDeadlineSeams(
            household.Planning.AskAsync,
            new FileMealWeekRecords(Path.Combine(folder, RecordFile)),
            new FileMealDecisionRecords(Path.Combine(folder, DecisionFile)),
            new HouseholdMealAnswers(household.Service),
            new HouseholdTemplateLibrary(household.Service),
            new HouseholdTasteNotes(household.Service),
            new UnavailableTasteJudge(),
            new BodyMealLog(new BodyLog(appContext)),
            new MealDeadlineCalendar(calendar.CalendarWriter, calendar.EventEditor, calendar.WrittenKeys));
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
