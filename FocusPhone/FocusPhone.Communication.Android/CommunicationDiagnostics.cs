using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Codex.Domain.Diagnostics;

namespace FocusPhone.Communication;

/// <summary>
/// The Communication head's diagnostics layer, on top of the shared one-shot
/// <see cref="Ui.CrashReporter"/>. It keeps a rolling, reviewable
/// <see cref="DiagnosticLog"/> of non-fatal failures (background email sync, the
/// message bridge, timeline-store problems) that never crash the app, plus a copy
/// of each unhandled crash, so the owner can review and share a history from the
/// Settings → Diagnostics screen. There is no ADB or logcat after cutover
/// (CUTOVER.md §17), so this on-device log is the only window into these faults.
///
/// Install once from <see cref="CommunicationApplication.OnCreate"/> (after the
/// shared CrashReporter). The log is rooted at the app's FilesDir, which the
/// WorkManager sync process shares with the UI process, so background failures
/// land in the same file the screen reads.
/// </summary>
public static class CommunicationDiagnostics
{
    private const string Label = "Communication";

    private static DiagnosticLog? log;
    private static CrashLogStore? crashStore;

    public static void Install(Application app)
    {
        var directory = app.FilesDir?.AbsolutePath;
        if (directory is null)
        {
            return;
        }
        log = new DiagnosticLog(directory);
        crashStore = new CrashLogStore(directory);

        // Append each crash to the rolling history as well — the shared
        // CrashReporter keeps only the latest crash for the one-shot next-launch
        // popup, which is deleted once shown.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => RecordCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => RecordCrash(e.Exception);
    }

    /// <summary>
    /// Records a non-fatal failure that would otherwise be swallowed into an
    /// inaccessible logcat. Callers keep their existing <c>Android.Util.Log</c>
    /// line as a live-debugging mirror; this is the durable, shareable copy.
    /// </summary>
    public static void LogWarning(string source, string message, Exception? exception = null)
    {
        if (log is null)
        {
            return;
        }
        try
        {
            log.Append(DiagnosticLogFormatter.FromException(
                DateTimeOffset.Now, DiagnosticLevel.Warning, source, message, exception));
        }
        catch
        {
            // Diagnostics must never throw and mask the caller's own flow.
        }
    }

    public static bool HasEntries => log?.HasEntries ?? false;

    public static string ReadAll() => log?.ReadAll() ?? string.Empty;

    public static void Clear() => log?.Clear();

    /// <summary>Reverses a <see cref="Clear"/> within the undo window (DESIGN.md §7).</summary>
    public static void Restore(string content) => log?.RestoreRaw(content);

    /// <summary>
    /// The shareable export bundle: a device snapshot, the full rolling log, and
    /// the latest captured crash (if the one-shot record hasn't been consumed yet).
    /// </summary>
    public static string BuildExport(Context context)
    {
        var version = "unknown";
        try
        {
            var info = context.PackageManager?.GetPackageInfo(context.PackageName!, (PackageInfoFlags)0);
            version = info?.VersionName ?? "unknown";
        }
        catch
        {
            // A missing version name must not stop the owner sharing the log.
        }

        var reportContext = new DiagnosticsContext(
            Label,
            version,
            $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}",
            DateTimeOffset.Now);
        return DiagnosticsReport.Build(reportContext, ReadAll(), crashStore?.ReadLatest());
    }

    private static void RecordCrash(Exception? exception)
    {
        if (log is null || exception is null)
        {
            return;
        }
        try
        {
            log.Append(DiagnosticLogFormatter.FromException(
                DateTimeOffset.Now, DiagnosticLevel.Crash, Label, "Unhandled exception", exception));
        }
        catch
        {
            // Runs inside an already-dying process; losing the record beats a
            // second exception thrown from the handler.
        }
    }
}
