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
/// (<see cref="WorkoutWeekDeliverySeams"/>). A recipe goes an hour before its cook block, so the run that plans a week cannot send it; the recipes of the week in progress are sent by
/// <see cref="TryRecipesAsync"/> (D120, closing D116).</para>
///
/// <para><b>The worker.</b> <see cref="MealWeekWorker"/> calls <see cref="PassAsync"/>, which looks at the stamp and the two kept records and runs only what is due: this
/// run, then the deadline step (<see cref="TryDecideAsync"/>), each through <see cref="RunGate.Shared"/>. This run plans the week <i>after</i> the one that
/// holds now, so it never reaches a recipe of the week in progress; <b>the recipes entry does (D120)</b>: <see cref="TryRecipesAsync"/> takes the week already kept, plans nothing, places nothing and
/// sends no sheet, and the pass asks for it only while a recipe is inside its hour and not yet on the tablet.</para>
///
/// <para><b>The other phone's week (D126-D128).</b> The run publishes the week it kept to the household file (<see cref="HouseholdWeekPlanPublisher"/>, planning phone only), and the
/// recipes part also serves this phone's tablet from the weeks the lease holder published (<see cref="MealSharedWeekRun"/>: no lease, no calendar, no saved week, no write), so the partner's phone
/// is no longer silent. The pass learns of such a week from the household file's local copy (<see cref="SharedPlansOf"/>).</para>
///
/// <para><b>The taste profile (D133)</b> is read from the household file each time (<see cref="TasteFromHousehold"/>: the roster, the box, the ratings the read-back writes and the notes the deadline step
/// teaches); with none kept it plans without one, as before.</para>
///
/// <para><b>Not yet here:</b> a source for the evenings the children eat at home, the Cook this soon mark, and an approval step. Nothing is swallowed
/// here: a seam's exception propagates to whoever calls. Device-gated: nothing here has met Google Calendar or a phone.</para>
/// </summary>
public static class MealWeekServices
{
    public const string FolderName = "meal-week";
    public const string HistoryFile = "cooked-history.json";
    public const string RecordFile = "planned-weeks.json";
    public const string DeliveredFile = "delivered-documents.json";
    public const string DecisionFile = "decided-weeks.json";
    public const string StampFile = "pass-stamp.json";

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
        var prepared = Prepare(appContext);

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

    /// <summary>
    /// What a pass is for: the recipes of the week in progress (only while one is inside its hour), the meal run, then the deadline step, whichever of them is due (<see cref="MealWeekPass"/>,
    /// which first reads the stamp and the two kept records, and the record of deliveries only while a recipe's hour is open, and runs nothing when nothing is due). Each part goes through
    /// <see cref="RunGate.Shared"/> by <see cref="TryRecipesAsync"/>, <see cref="TryRunAsync"/> and <see cref="TryDecideAsync"/>, so a busy gate is a typed step and not a throw. The result is returned whole; the caller decides whether a fault fails the pass (<see cref="MealPassResult.ThrowIfFaulted"/>).
    /// </summary>
    public static async Task<MealPassResult> PassAsync(Context context, MealPassTrigger trigger, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();

        var appContext = context.ApplicationContext ?? context;
        var folder = FolderOf(appContext);
        var prepared = Prepare(appContext) as MealWeekPreparation.Ready;
        var seams = new MealPassSeams(
            new WeeksTheHouseholdDecides(
                new FileMealWeekRecords(Path.Combine(folder, RecordFile)), new HouseholdWeekPlans(ConstitutionHousehold.For(appContext).Service)),
            DecisionRecords(appContext),
            new FileMealPassStamps(Path.Combine(folder, StampFile)),
            token => TryRunAsync(appContext, token),
            token => TryDecideAsync(appContext, token),
            new BodyMealLog(new BodyLog(appContext)),
            new FileDeliveredMealDocuments(Path.Combine(folder, DeliveredFile)),
            prepared is null ? null : MealRecipeHour.TabletPerson(prepared.Inputs.Rules, prepared.Inputs.DeliverFor),
            token => TryRecipesAsync(appContext, token),
            () => SharedPlansOf(appContext));
        var zone = prepared?.Inputs.Rules.Zone ?? TimeZoneInfo.Local;
        return await MealWeekPass.RunAsync(trigger, DateTimeOffset.Now, zone, seams, cancellation);
    }

    /// <summary>
    /// The recipes of the week in progress, once it has its turn (D120): the documents step on the kept week, for each cooked dinner of this phone's adult whose recipe is inside its hour and
    /// not yet on the tablet. It plans nothing, writes nothing to a calendar and changes no kept week; <see cref="GateResult{T}.Busy"/> when another placement run held the gate for the
    /// whole wait (the next pass inside the hour tries again). It goes through the gate like the other parts because the tablet and the read-back's record are shared with the workout run.
    /// </summary>
    public static Task<GateResult<MealRecipesOutcome>> TryRecipesAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();
        return RunGate.Shared.RunAsync(token => RecipesAloneAsync(context, token), RunGate.DefaultWait, cancellation);
    }

    private static async Task<MealRecipesOutcome> RecipesAloneAsync(Context context, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var appContext = context.ApplicationContext ?? context;
        var log = new BodyMealLog(new BodyLog(appContext));
        if (Prepare(appContext) is not MealWeekPreparation.Ready ready)
        {
            // No household setup: no rules and no tablet to name. A typed stop, tried again once the setup reads.
            var stopped = new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.NotConfigured, "There is no household setup on this phone, so no recipe was sent.");
            log.Outcome(MealRecipesTrail.LogPhrase(stopped));
            return stopped;
        }

        var folder = FolderOf(appContext);
        var household = ConstitutionHousehold.For(appContext);
        var plans = new HouseholdWeekPlans(household.Service);
        var box = new HouseholdRecipeBox(household.Service);
        var documents = AssembleDocuments(appContext);

        // Two halves of one part: this phone's own kept weeks (without a week the other phone has published, which is theirs to deliver), then the weeks the
        // lease holder published to the household file for this phone's tablet (D127). Neither can plan, place or save; the second only reads the file.
        var own = await MealRecipeHourRun.RunAsync(
            ready.Inputs,
            new MealRecipeSeams(new KeptWeeksBesideTheHousehold(new FileMealWeekRecords(Path.Combine(folder, RecordFile)), plans), box, new TasteFromHousehold(household.Service), documents, log),
            cancellation);
        var theirs = await MealSharedWeekRun.RunAsync(ready.Inputs, new MealSharedSeams(plans, box, new TasteFromHousehold(household.Service), documents, log), cancellation);
        return MealRecipesOutcomes.Combine(own, theirs);
    }

    // The weeks another phone published, read once per pass from this phone's copy of the household file (a local read). Never throws: a file that will not open is "none".
    private static IReadOnlyList<SharedWeekPlan> SharedPlansOf(Context appContext) =>
        new HouseholdWeekPlans(ConstitutionHousehold.For(appContext).Service).Read() is WeekPlansRead.Held held
            ? SharedWeeks.FromOthers(held.Set, held.Me)
            : [];

    /// <summary>The deadline step, once it has its turn at the calendars (it takes a denied evening's cook block off, through the same record of written keys); <see cref="GateResult{T}.Busy"/> when another placement run held them for the whole wait.</summary>
    public static Task<GateResult<MealDeadlineOutcome>> TryDecideAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();
        return RunGate.Shared.RunAsync(token => DecideAloneAsync(context, token), RunGate.DefaultWait, cancellation);
    }

    private static async Task<MealDeadlineOutcome> DecideAloneAsync(Context context, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var appContext = context.ApplicationContext ?? context;
        var prepared = Prepare(appContext);
        if (prepared is MealWeekPreparation.Stop stop)
        {
            // A kept week and no household setup: the rules (and so the zone) are unknown, so nothing is decided. A typed stop, tried again once the setup reads.
            var stopped = new MealDeadlineOutcome.Stopped(MealDeadlineStoppedBecause.NotConfigured, stop.Outcome.Sentence);
            new BodyMealLog(new BodyLog(appContext)).Outcome(MealDeadlineTrail.LogPhrase(stopped));
            return stopped;
        }

        var ready = (MealWeekPreparation.Ready)prepared;
        return await MealDeadlineStep.RunAsync(new MealDeadlineInputs(ready.Inputs.Now, ready.Inputs.Rules), AssembleDeadline(appContext), cancellation);
    }

    // The household's own setup joined with the clock, read afresh each time: the file is small and local, and a pass must see the setup as it is now.
    private static MealWeekPreparation Prepare(Context appContext)
    {
        var workScheduleJson = new ConstitutionConnections(appContext).WorkScheduleJson;
        return MealWeekJoin.Prepare(
            PlanningSetupFile.Read(Path.Combine(WorkoutWeekPaths.Folder(NoBackupRoot(appContext)), WorkoutWeekCalendarSeams.SetupFile)),
            string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson,
            TimeZoneInfo.Local,
            DateTimeOffset.Now);
    }

    /// <summary>The saved weeks as the Meals tab and Today read them.</summary>
    public static IMealWeekSource Source(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var appContext = context.ApplicationContext ?? context;

        // Her phone shows the week the planning phone published when it kept none of its own (D154).
        return new MealWeekRecordSource(new WeeksTheHouseholdDecides(
            new FileMealWeekRecords(Path.Combine(FolderOf(appContext), RecordFile)), new HouseholdWeekPlans(ConstitutionHousehold.For(appContext).Service)));
    }

    /// <summary>
    /// The run's seams. The taste profile is the household file's (<see cref="TasteFromHousehold"/>; none kept plans without one). The documents step reuses the workout run's delivery side
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
        var documents = AssembleDocuments(appContext);
        return new MealWeekSeams(
            household.Planning.OpenTheWeekAsync,
            new HouseholdRecipeBox(household.Service),
            new MealHistoryStore(Path.Combine(folder, HistoryFile)),
            new TasteFromHousehold(household.Service),
            calendar.Calendars,
            new MealWithFallback(new NoMealAuthor(), new TrailMealLog(log)),
            calendar.CalendarWriter,
            calendar.WrittenKeys,
            calendar.KeyAssignments,
            new FileMealWeekRecords(Path.Combine(folder, RecordFile)),
            log,
            documents,
            new MealTemplateSeams(new HouseholdTemplateLibrary(household.Service), TemplateAuthorServices.AuthorOf(appContext)),
            Publisher: new HouseholdWeekPlanPublisher(household.Service),
            Answers: new HouseholdMealAnswers(household.Service),
            AteEstimates: new FileAteEstimates(Path.Combine(appContext.NoBackupFilesDir!.AbsolutePath, "intake", "intake-estimates.json")),
            Decisions: DecisionRecords(appContext));
    }

    /// <summary>
    /// What the documents step reaches: the workout run's tablet side (<see cref="WorkoutWeekDeliverySeams"/>) and the meal run's own record of what it has sent, the roster and pantry over the
    /// household file, Index's budget and the holiday calendar that cannot be asked. Shared by the weekly run and the recipes entry, so a title and a fingerprint mean the same page to both.
    /// </summary>
    public static MealDocumentSeams AssembleDocuments(Context appContext)
    {
        ArgumentNullException.ThrowIfNull(appContext);

        var household = ConstitutionHousehold.For(appContext);
        var delivery = WorkoutWeekDeliverySeams.Create(appContext);
        return new MealDocumentSeams(
            delivery.Delivery,
            delivery.Tablet,
            delivery.InkProbe,
            delivery.Expected,
            new FileDeliveredMealDocuments(Path.Combine(FolderOf(appContext), DeliveredFile)),
            new HouseholdMealRoster(household.Service),
            new HouseholdMealPantry(household.Service),
            new FileMonthlyBudgets(Path.Combine(NoBackupRoot(appContext), FileMonthlyBudgets.FileName)),
            new UnavailableHolidayCalendar(),
            new BodyMealLog(new BodyLog(appContext)));
    }

    /// <summary>The record of decided weeks, a small file beside the kept weeks: the deadline step writes it, and the sheet and the Meals tab's share read the denied evenings' shopping lines from it (D112).</summary>
    public static IMealDecisionRecords DecisionRecords(Context appContext)
    {
        ArgumentNullException.ThrowIfNull(appContext);
        return new FileMealDecisionRecords(Path.Combine(FolderOf(appContext.ApplicationContext ?? appContext), DecisionFile));
    }

    /// <summary>
    /// The seams of the deadline step (<see cref="MealDeadlineStep"/>), the part of the meal week that runs once the sheet's deadline has passed. The lease, the kept
    /// weeks, the household file's answers, library and taste notes are this install's own; the record of decided weeks is a small file beside the kept weeks; the calendar side (the meal key family's writer, the narrow editor and the written keys the meal run placed with) takes a denied evening's cook block off (D111). <b>The taste
    /// judge is the Claude judge (<see cref="TasteJudgeServices"/>, D133):</b> with no key, or while the service cannot answer, written words are recorded as unjudged and asked again on a later pass, a bounded number of times
    /// (<see cref="TasteJudgeLimits"/>). The worker reaches it through <see cref="TryDecideAsync"/>, and nothing here holds a secret.
    /// </summary>
    public static MealDeadlineSeams AssembleDeadline(Context appContext)
    {
        ArgumentNullException.ThrowIfNull(appContext);

        var household = ConstitutionHousehold.For(appContext);
        var folder = FolderOf(appContext);
        var calendar = WorkoutWeekCalendarSeams.Create(appContext, KeyFamily.HouseholdMeal);
        return new MealDeadlineSeams(
            household.Planning.OpenTheWeekAsync,
            new WeeksTheHouseholdDecides(new FileMealWeekRecords(Path.Combine(folder, RecordFile)), new HouseholdWeekPlans(household.Service)),
            DecisionRecords(appContext),
            new HouseholdMealAnswers(household.Service),
            new HouseholdTemplateLibrary(household.Service),
            new HouseholdTasteNotes(household.Service),
            TasteJudgeServices.JudgeOf(appContext, new HouseholdRecipeBox(household.Service)),
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
