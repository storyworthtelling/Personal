using Android.App;
using Android.Content;
using Android.Content.PM;
using Codex.Domain.Diagnostics;
using Codex.Domain.Post;
using Storyworthtelling.Diagnostics;

namespace Post;

/// <summary>
/// The Post head's diagnostics layer: a rolling, reviewable <see cref="DiagnosticLog"/> of non-fatal
/// failures (background email sync, the message bridge, timeline-store problems) that never crash the app,
/// every crash (through the shared <see cref="Ui.CrashReporter"/>), and the texts trail — reviewed and
/// shared from Settings → Diagnostics. There is no ADB or logcat after cutover (CUTOVER.md §17), so this
/// on-device log is the only window into these faults.
///
/// On the log loop (LOG-LOOP-PLAN.md, Phase 1 pilot): both logs seal into the <see cref="LogOutbox"/>
/// rather than discarding their older half, the weekly <see cref="LogUploadWorker"/> carries the sealed
/// segments to Drive and clears them once Drive has a confirmed copy, and the tap watch, the Gmail sign-in
/// trail, exit reasons and the worker trail all write here.
///
/// Install once, first thing, from <see cref="PostApplication.OnCreate"/>. The log is rooted at the app's
/// FilesDir, which the WorkManager sync process shares with the UI process, so background failures land in
/// the same file the screen reads.
/// </summary>
public static class PostDiagnostics
{
    public const string Label = "Post";

    /// <summary>The app name in every batch header and file name.</summary>
    public const string LoopApp = "post";

    // The texts trail: a line per text that reaches Post, by whichever route, so a late or
    // missing text can be traced afterwards. Its own file and cap, because a line per text
    // in the failure log would roll the failure history out within a day.
    private const string TextsFileName = "texts.log";
    private const long TextsMaxBytes = 96 * 1024;
    private const long LogMaxBytes = 128 * 1024;

    private const string PreferencesName = "log-loop";
    private const string InstallIdKey = "installId";

    private static DiagnosticLog? log;
    private static DiagnosticLog? texts;
    private static CrashLogStore? crashStore;
    private static LogOutbox? outbox;

    /// <summary>The failure log, which the crash reporter appends every crash to.</summary>
    public static DiagnosticLog? Log => log;

    /// <summary>Where sealed segments wait for Drive.</summary>
    public static LogOutbox? Outbox => outbox;

    /// <summary>Every live log the upload seals.</summary>
    public static IReadOnlyList<string> LivePaths =>
        new[] { log?.LivePath, texts?.LivePath }.OfType<string>().ToList();

    public static void Install(Application app)
    {
        var directory = app.FilesDir?.AbsolutePath;
        if (directory is null)
        {
            return;
        }
        outbox = new LogOutbox(directory);
        log = new DiagnosticLog(directory, LogMaxBytes, DiagnosticLog.FileName, outbox);
        texts = new DiagnosticLog(directory, TextsMaxBytes, TextsFileName, outbox);
        crashStore = new CrashLogStore(directory);
    }

    /// <summary>
    /// Records a non-fatal failure that would otherwise be swallowed into an
    /// inaccessible logcat. Callers keep their existing <c>Android.Util.Log</c>
    /// line as a live-debugging mirror; this is the durable, shareable copy.
    /// </summary>
    public static void LogWarning(string source, string message, Exception? exception = null) =>
        Write(DiagnosticLevel.Warning, source, message, exception);

    /// <summary>A decision or outcome worth having in the weekly record (plan §4.5) that is not a failure.</summary>
    public static void LogInfo(string source, string message) => Write(DiagnosticLevel.Info, source, message, null);

    /// <summary>A tap-watch verdict: the problem line with the last taps as its context (plan §4.1).</summary>
    public static void LogTap(TapVerdict verdict) => Write(DiagnosticLevel.Warning, "Tap", verdict.ToLogText(), null);

    /// <summary>A finished sign-in trail and its diagnosis (plan §4.2).</summary>
    public static void LogSignIn(SignInTrail trail) =>
        Write(trail.Succeeded ? DiagnosticLevel.Info : DiagnosticLevel.Warning, "SignIn", trail.ToLogText(), null);

    /// <summary>
    /// Runs a worker's body on the worker trail (plan §4.3): start, end, result and duration, or what it
    /// threw — rethrown, so WorkManager sees exactly what it saw before.
    /// </summary>
    public static T Worker<T>(string name, Func<T> body)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        LogInfo("Worker", WorkerTrail.StartedLine(name));
        try
        {
            var result = body();
            LogInfo("Worker", WorkerTrail.FinishedLine(name, ResultName(result), started.Elapsed));
            return result;
        }
        catch (Exception e)
        {
            Write(DiagnosticLevel.Warning, "Worker", WorkerTrail.ThrewLine(name, started.Elapsed, e), e);
            throw;
        }
    }

    /// <summary>Once per process start, so every later line can be tied to a build (plan §4.3).</summary>
    public static void LogLaunch(Context context)
    {
        var (version, code) = Version(context);
        LogInfo("Launch", LaunchLine.Format(Label, version, code, Android.OS.Build.VERSION.Release ?? "?", (int)Android.OS.Build.VERSION.SdkInt));
    }

    /// <summary>Who is uploading. Post runs only on the owner's locked phone, so the phone is the owner's.</summary>
    public static LogInstall LoopInstall(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
        var id = preferences.GetString(InstallIdKey, null);
        if (string.IsNullOrEmpty(id))
        {
            id = LogInstall.NewInstallId();
            preferences.Edit()!.PutString(InstallIdKey, id)!.Apply();
        }
        var (version, code) = Version(context);
        return new LogInstall(LoopApp, id, LogPhone.Owner, version, code);
    }

    /// <summary>
    /// Adds a line to the texts trail. The line is built by <see cref="TextTrail"/>, which
    /// keeps message content and whole numbers out of it — the trail is meant to be shared.
    /// </summary>
    public static void LogText(string source, string line)
    {
        if (texts is null)
        {
            return;
        }
        try
        {
            texts.Append(new DiagnosticEntry(DateTimeOffset.Now, DiagnosticLevel.Info, source, line));
        }
        catch
        {
            // Diagnostics must never throw and mask the caller's own flow — least of all
            // from inside a broadcast receiver holding an incoming text.
        }
    }

    public static bool HasEntries => log?.HasEntries ?? false;

    /// <summary>Everything since the last confirmed upload: sealed segments, then the live file.</summary>
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
        var reportContext = new DiagnosticsContext(
            Label,
            Version(context).Name,
            $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}",
            DateTimeOffset.Now);
        // Field notes ride along with the failures. They are what turns one run of the
        // on-device test plan into the numbers the deferred work needs — how much of the
        // owner's real mail is HTML-only, what their attachments are, how far the archive
        // grows, how many contacts have no face, and whether date detection finds anything
        // worth proposing.
        var report = DiagnosticsReport.Build(reportContext, ReadAll(), crashStore?.ReadLatest());
        return $"{report}\n\n{FieldNotesStore.Load(context).Render()}\n\n{TextsSection(context)}";
    }

    private static void Write(string level, string source, string message, Exception? exception)
    {
        if (log is null)
        {
            return;
        }
        try
        {
            log.Append(DiagnosticLogFormatter.FromException(DateTimeOffset.Now, level, source, message, exception));
        }
        catch
        {
            // Diagnostics must never throw and mask the caller's own flow.
        }
    }

    private static string ResultName<T>(T result)
    {
        var text = result?.ToString() ?? "null";
        var brace = text.IndexOf('{');
        return (brace > 0 ? text[..brace] : text).Trim();
    }

    private static (string Name, long Code) Version(Context context)
    {
        try
        {
            var info = context.PackageManager?.GetPackageInfo(context.PackageName!, (PackageInfoFlags)0);
            return (info?.VersionName ?? "unknown", info?.LongVersionCode ?? 0);
        }
        catch
        {
            // A missing version must not stop the owner sharing the log.
            return ("unknown", 0);
        }
    }

    // Which routes a text has into Post right now, then the trail of what actually arrived.
    private static string TextsSection(Context context)
    {
        string status;
        try
        {
            status = TextsStatusReader.Read(context).Render();
        }
        catch (Exception e)
        {
            status = $"Could not read the texts status: {e.Message}";
        }
        var trail = texts?.ReadAll().TrimEnd() ?? string.Empty;
        return "=== Texts ===\n" + status + "\n\n--- Texts trail (oldest first) ---\n"
            + (trail.Length == 0 ? "Nothing recorded yet." : trail);
    }
}
