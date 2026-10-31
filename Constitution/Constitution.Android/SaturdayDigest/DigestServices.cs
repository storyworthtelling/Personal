using Android.Content;
using Constitution.Body;
using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Infrastructure.Workouts;
using Constitution.WorkoutDelivery;
using Constitution.Infrastructure.Workouts;
using Constitution.WeeklyMeals;
using Constitution.WorkoutWeek;
using Constitution.ReadBack;


namespace Constitution.SaturdayDigest;

/// <summary>
/// The Saturday digest's head door: wires <see cref="SaturdayDigestRun"/> to this phone's real stores (the saved workout week and the skip ledger, Body's local store, the kept meal weeks)
/// and its tablet, and takes <see cref="RunGate.Shared"/> for the run because the tablet and the read-back's record are shared with the workout and meal runs. The digest goes to the adult whose
/// tablet this phone has, read afresh from the household setup; with no setup it stops, typed, and tries again. Device-gated: nothing here has met the reMarkable cloud or a phone.
/// </summary>
public static class DigestServices
{
    public const string FolderName = "saturday-digest";
    public const string StampFile = "digest-stamps.json";

    public const string NoTablet = "There is no household setup on this phone naming whose tablet it is, so no Saturday digest was sent.";

    public static async Task<GateResult<DigestOutcome>> TryRunAsync(Context context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();
        var appContext = context.ApplicationContext ?? context;
        return await RunGate.Shared.RunAsync(token => RunAloneAsync(appContext, token), RunGate.DefaultWait, cancellation);
    }

    private static async Task<DigestOutcome> RunAloneAsync(Context appContext, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (MealWeekServices.TabletOwnerOf(appContext) is not { } owner)
        {
            return new DigestOutcome.NotReady(NoTablet);
        }

        // The cheap test first: not Saturday means no stores are opened at all.
        var now = DateTimeOffset.Now;
        if (DigestSchedule.DueSaturday(now, owner.Zone) is null)
        {
            return new DigestOutcome.NotDue();
        }

        var seams = WorkoutWeekDeliverySeams.Create(appContext);
        var root = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the digest's stamp in.");
        var folder = WorkoutWeekPaths.Folder(root);
        var sources = new LiveDigestSources(
            new FileWorkoutWeekReports(Path.Combine(folder, WorkoutWeekFiles.ReportFile)),
            ReadBackServices.LedgerOf(appContext),
            BodyServices.CreateStore(appContext),
            MealWeekServices.KeptWeeksOf(appContext));
        var run = new SaturdayDigestRun(
            sources, seams.Delivery, seams.Expected, new FileDigestStamps(Path.Combine(root, FolderName, StampFile)));
        var outcome = await run.RunAsync(now, owner.Zone, owner.Person, cancellation);
        new BodyLog(appContext).Outcome(DigestTrail.LogPhrase(outcome));
        return outcome;
    }
}
