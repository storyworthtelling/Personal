using Android.Content;
using Codex.Ui.Voice;
using Constitution.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Services;
using Constitution.WorkoutDelivery;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;
using Xamarin.Android.Net;

namespace Constitution.ReadBack;

/// <summary>
/// Builds the read-back's object graph on the device, once per run, and says where its files live. Only wiring; the
/// decisions are in Domain and Infrastructure and are tested there. Everything is in the app's no-backup folder
/// (<c>read-back/</c>), so no copy of the owner's answers or pages goes to a cloud backup.
///
/// <para>The reMarkable pairing and the Claude key are read from this install's connections as they are now, so a pairing
/// repaired since the last run takes effect on the next. The device token goes to the shared auth service and nowhere
/// else; neither it nor the key is logged.</para>
/// </summary>
public sealed class ReadBackServices
{
    // A page of several boxes through a vision model takes the better part of a minute; the shared default of 100 s is enough for the tablet.
    private static readonly TimeSpan VisionTimeout = TimeSpan.FromMinutes(3);

    private ReadBackServices(ReadBackService service, ReadBackStatusFile status)
    {
        Service = service;
        Status = status;
    }

    public ReadBackService Service { get; }

    public ReadBackStatusFile Status { get; }

    /// <summary>The graph for this device.</summary>
    public static ReadBackServices Create(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var tablet = new RemarkableAnnotatedPageSource(TabletOf(appContext));
        var vision = VisionOf(appContext);
        var hooks = SettledOf(appContext);

        var service = new ReadBackService(
            ExpectedOf(appContext),
            tablet,
            vision,
            Results(appContext),
            LedgerOf(appContext),
            InkProofOf(appContext),
            new ReadBackNoticeBoard(),
            new ReadBackLog(appContext),
            () => DateTimeOffset.Now,
            hooks);
        return new ReadBackServices(service, StatusOf(appContext));
    }

    /// <summary>
    /// Who is told when a page is read, in this order: first what the workouts taught the plan (the ladders, the soreness, the baselines, over the
    /// skip ledger the read-back itself keeps), then what the meal documents said, written into the household file this phone holds. Each is told
    /// whatever the other did; a failure in either keeps the page and the next morning tries again.
    /// </summary>
    public static SettledHooks SettledOf(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        return new SettledHooks(
            new FileWorkoutLearning(LedgerOf(appContext), WorkoutWeekPaths.Learning(Root(appContext))),
            new MealReadBackSettled(ConstitutionHousehold.For(appContext).Service));
    }

    /// <summary>
    /// The tablet client bound to this phone's pairing, read from this install's connections as they are now. Delivery builds
    /// its client here too: the device token has one path, and goes to the shared auth service and nowhere else.
    /// </summary>
    public static IRemarkableClient TabletOf(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var tabletHttp = new HttpClient(new AndroidMessageHandler());
        var auth = new RemarkableAuthService(
            tabletHttp,
            Options.Create(new RemarkableOptions { DeviceToken = new ConstitutionConnections(appContext).RemarkableDeviceToken }),
            new CredentialHealthStore());
        return new RemarkableStorageClient(tabletHttp, auth);
    }

    /// <summary>The page reader. With no Claude key it answers that it cannot read yet, which callers treat as "unknown".</summary>
    public static ClaudeInkVision VisionOf(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var visionHttp = new HttpClient(new AndroidMessageHandler()) { Timeout = VisionTimeout };
        return new ClaudeInkVision(
            visionHttp, Options.Create(new ClaudeVisionOptions { ApiKey = new ConstitutionConnections(appContext).ClaudeApiKey }), new CredentialHealthStore());
    }

    /// <summary>The record of whether a pen stroke has ever come through this tablet's round trip.</summary>
    public static FileInkProof InkProofOf(Context context) => new(ReadBackPaths.Proof(Root(context)));

    /// <summary>The delivered documents waiting for their ink; delivery records each one here once the tablet has taken it.</summary>
    public static FileExpectedDocuments ExpectedOf(Context context) => new(ReadBackPaths.Expected(Root(context)));

    /// <summary>Each read document's saved result, and the questions still waiting.</summary>
    public static FileResolutionStore Results(Context context) => new(ReadBackPaths.Results(Root(context)));

    /// <summary>The skip ledger. The planner reads it from here too: it is the one copy.</summary>
    public static FileLedgerStore LedgerOf(Context context) => new(ReadBackPaths.Ledger(Root(context)));

    /// <summary>How the last morning ended, for the Diagnostics export.</summary>
    public static ReadBackStatusFile StatusOf(Context context) => new(ReadBackPaths.Status(Root(context)), () => DateTimeOffset.Now);

    private static string Root(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        return appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the read-back's files in.");
    }
}
