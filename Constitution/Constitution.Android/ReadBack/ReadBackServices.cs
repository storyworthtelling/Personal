using Android.Content;
using Codex.Ui.Voice;
using Constitution.Services;
using Constitution.Infrastructure.ReadBack;
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
    private const string FolderName = "read-back";
    private const string ExpectedFile = "expected.json";
    private const string ResultsFolder = "results";
    private const string LedgerFile = "skip-ledger.json";
    private const string ProofFile = "ink-proof.json";
    private const string StatusFile = "status.json";

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
        var connections = new ConstitutionConnections(appContext);
        var health = new CredentialHealthStore();

        var tabletHttp = new HttpClient(new AndroidMessageHandler());
        var auth = new RemarkableAuthService(
            tabletHttp, Options.Create(new RemarkableOptions { DeviceToken = connections.RemarkableDeviceToken }), health);
        var tablet = new RemarkableAnnotatedPageSource(new RemarkableStorageClient(tabletHttp, auth));

        var visionHttp = new HttpClient(new AndroidMessageHandler()) { Timeout = VisionTimeout };
        var vision = new ClaudeInkVision(visionHttp, Options.Create(new ClaudeVisionOptions { ApiKey = connections.ClaudeApiKey }), health);

        var directory = Directory(appContext);
        var service = new ReadBackService(
            ExpectedOf(appContext),
            tablet,
            vision,
            Results(appContext),
            LedgerOf(appContext),
            new FileInkProof(Path.Combine(directory, ProofFile)),
            new ReadBackNoticeBoard(),
            new ReadBackLog(appContext),
            () => DateTimeOffset.Now);
        return new ReadBackServices(service, StatusOf(appContext));
    }

    /// <summary>The delivered documents waiting for their ink; delivery records each one here once the tablet has taken it.</summary>
    public static FileExpectedDocuments ExpectedOf(Context context) => new(Path.Combine(Directory(context), ExpectedFile));

    /// <summary>Each read document's saved result, and the questions still waiting.</summary>
    public static FileResolutionStore Results(Context context) => new(Path.Combine(Directory(context), ResultsFolder));

    /// <summary>The skip ledger. The planner reads it from here too: it is the one copy.</summary>
    public static FileLedgerStore LedgerOf(Context context) => new(Path.Combine(Directory(context), LedgerFile));

    /// <summary>How the last morning ended, for the Diagnostics export.</summary>
    public static ReadBackStatusFile StatusOf(Context context) => new(Path.Combine(Directory(context), StatusFile), () => DateTimeOffset.Now);

    private static string Directory(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var root = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the read-back's files in.");
        return Path.Combine(root, FolderName);
    }
}
