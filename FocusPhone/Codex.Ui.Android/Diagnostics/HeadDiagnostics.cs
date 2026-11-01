using Android.App;
using Android.Content;
using Android.Content.PM;
using Codex.Domain.Diagnostics;
using Codex.Ui.Voice;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;
using Storyworthtelling.Voice.Secrets;

namespace Codex.Ui;

/// <summary>What a head tells the shared diagnostics about itself.</summary>
/// <param name="Label">The app's name in log lines and the crash popup ("Flyleaf").</param>
/// <param name="LoopApp">The app's name in every batch header and file name ("flyleaf"). A wire name.</param>
/// <param name="SendLogsNowAction">The head's own key-action id for Send logs now.</param>
/// <param name="DriveToken">The full-<c>drive</c> token the head already uses for the vault.</param>
/// <param name="SignedIn">Whether that account is signed in; no account, no Drive, no upload.</param>
/// <param name="Phone">
/// <see cref="LogPhone.Owner"/> for the locked FocusPhone's heads, which only ever run on the owner's phone;
/// null for an app on both phones, which reads it from the shared vault's ownership at each upload.
/// </param>
/// <param name="Directory">Where the logs live; null is FilesDir (Constitution keeps its in NoBackupFilesDir).</param>
/// <param name="ExtraLogs">The head's other trails, each its own file and cap, all sealed and uploaded.</param>
/// <param name="ScheduleUpload">False for Post, whose own upload worker shipped first under its own frozen name.</param>
public sealed record HeadSetup(
    string Label,
    string LoopApp,
    string SendLogsNowAction,
    Func<Task<string>> DriveToken,
    Func<bool> SignedIn,
    string? Phone = LogPhone.Owner,
    string? Directory = null,
    IReadOnlyList<(string FileName, long MaxBytes)>? ExtraLogs = null,
    bool ScheduleUpload = true);

/// <summary>
/// The log loop on a native head, once (LOG-LOOP-PLAN.md §3, §4.1–4.4). Installed first thing from the head's
/// Application: the rolling log sealing into the <see cref="LogOutbox"/>, the shared crash reporter appending
/// every crash to it, the tap watch, the launch line and every exit since the last launch, and the weekly
/// upload. Every head used to keep its own copy of most of this; two copies of "when is it safe to delete a
/// log" is the drift that loses logs (plan A5), so there is one.
/// <para>
/// Diagnostics never break the app: every write swallows its own failure, and a head that was never
/// installed (a test process, a broadcast before Application.OnCreate) simply logs nothing.
/// </para>
/// </summary>
public sealed class HeadDiagnostics
{
    public const long LogMaxBytes = 128 * 1024;
    private const string PreferencesName = "log-loop";
    private const string InstallIdKey = "installId";
    private const string HistoryKey = "uploadHistory";
    private const string RefusedKey = "uploadRefused";

    private readonly Context app;
    private readonly HeadSetup setup;
    private readonly CrashLogStore crashStore;
    private readonly Dictionary<string, DiagnosticLog> logs = new(StringComparer.Ordinal);
    private int uploading;

    private HeadDiagnostics(Context app, HeadSetup setup, string directory)
    {
        this.app = app;
        this.setup = setup;
        Outbox = new LogOutbox(directory);
        Log = new DiagnosticLog(directory, LogMaxBytes, DiagnosticLog.FileName, Outbox);
        logs[DiagnosticLog.FileName] = Log;
        foreach (var (fileName, maxBytes) in setup.ExtraLogs ?? [])
        {
            logs[fileName] = new DiagnosticLog(directory, maxBytes, fileName, Outbox);
        }
        crashStore = new CrashLogStore(directory);
    }

    /// <summary>The installed head, or null before Install (or in a process that never ran it).</summary>
    public static HeadDiagnostics? Current { get; private set; }

    public HeadSetup Setup => setup;

    /// <summary>The failure log, which the crash reporter appends every crash to.</summary>
    public DiagnosticLog Log { get; }

    public LogOutbox Outbox { get; }

    /// <summary>Every live log the upload seals.</summary>
    public IReadOnlyList<string> LivePaths => logs.Values.Select(l => l.LivePath).ToList();

    /// <summary>One of the head's other trails, by file name.</summary>
    public DiagnosticLog Trail(string fileName) => logs[fileName];

    public static HeadDiagnostics Install(Application application, HeadSetup setup)
    {
        var directory = setup.Directory ?? application.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException($"{setup.Label} has no files directory to keep its logs in.");
        var head = new HeadDiagnostics(application, setup, directory);
        Current = head;

        CrashReporter.Install(application, setup.Label, head.Log);
        head.LogLaunch();
        ExitTrail.Record(application, (problem, line) =>
        {
            if (problem)
            {
                head.Warn("Exit", line);
            }
            else
            {
                head.Info("Exit", line);
            }
        });
        TapWatch.Install(head.Tap);
        // §4.2: a sign-in that left the app and heard nothing back is a NoReturn the moment any screen resumes.
        application.RegisterActivityLifecycleCallbacks(new SignInResumeCheck());
        if (setup.ScheduleUpload)
        {
            LoopUploadWorker.Schedule(application);
        }
        return head;
    }

    public void Warn(string source, string message, Exception? exception = null) =>
        Write(DiagnosticLevel.Warning, source, message, exception);

    public void Info(string source, string message) => Write(DiagnosticLevel.Info, source, message, null);

    /// <summary>A tap-watch verdict: the problem line with the last taps as its context (plan §4.1).</summary>
    public void Tap(TapVerdict verdict) => Write(DiagnosticLevel.Warning, "Tap", verdict.ToLogText(), null);

    /// <summary>A finished sign-in trail and its diagnosis (plan §4.2).</summary>
    public void SignIn(SignInTrail trail) =>
        Write(trail.Succeeded ? DiagnosticLevel.Info : DiagnosticLevel.Warning, "SignIn", trail.ToLogText(), null);

    /// <summary>A line on one of the head's other trails.</summary>
    public void Line(string fileName, string source, string line)
    {
        try
        {
            logs[fileName].Append(new DiagnosticEntry(DateTimeOffset.Now, DiagnosticLevel.Info, source, line));
        }
        catch
        {
            // Diagnostics must never throw and mask the caller's own flow.
        }
    }

    /// <summary>
    /// Runs a worker's body on the worker trail (plan §4.3): start, end, result and duration, or what it threw
    /// — rethrown, so WorkManager sees exactly what it saw before. A process with no head installed just runs it.
    /// </summary>
    public static T Worker<T>(string name, Func<T> body)
    {
        var head = Current;
        if (head is null)
        {
            return body();
        }
        var started = System.Diagnostics.Stopwatch.StartNew();
        head.Info("Worker", WorkerTrail.StartedLine(name));
        try
        {
            var result = body();
            head.Info("Worker", WorkerTrail.FinishedLine(name, ResultName(result), started.Elapsed));
            return result;
        }
        catch (Exception e)
        {
            head.Write(DiagnosticLevel.Warning, "Worker", WorkerTrail.ThrewLine(name, started.Elapsed, e), e);
            throw;
        }
    }

    public bool HasEntries => Log.HasEntries;

    /// <summary>Everything since the last confirmed upload: sealed segments, then the live file.</summary>
    public string ReadAll() => Log.ReadAll();

    public void Clear() => Log.Clear();

    public void Restore(string content) => Log.RestoreRaw(content);

    /// <summary>The shareable export: a snapshot, the log, the latest crash, and every other trail.</summary>
    public string BuildExport()
    {
        var context = new DiagnosticsContext(setup.Label, Version().Name, $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}", DateTimeOffset.Now);
        var report = DiagnosticsReport.Build(context, ReadAll(), crashStore.ReadLatest());
        var trails = logs.Where(l => l.Key != DiagnosticLog.FileName)
            .Select(l => $"=== {l.Key} ===\n{(l.Value.ReadAll().TrimEnd() is { Length: > 0 } t ? t : "Nothing recorded yet.")}");
        return string.Join("\n\n", new[] { report }.Concat(trails));
    }

    public UploadHistory History =>
        UploadHistory.Parse(Preferences().GetString(HistoryKey, null));

    /// <summary>The one sentence a Diagnostics screen shows about uploads, or null.</summary>
    public string? UploadStatus =>
        LogUploadStatus.Sentence(Preferences().GetBoolean(RefusedKey, false) ? LogUploadRefusal.WaitingForPassphrase : null, History.FailedWeeks);

    /// <summary>
    /// One upload run (plan §3.3): the head's live logs and outbox, the log folder in Drive over the head's own
    /// token, sealed under the key derived from the vault passphrase. The weekly worker and Send logs now both
    /// come here; two at once are one run.
    /// </summary>
    public async Task<LogUploadResult> UploadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref uploading, 1, 0) != 0)
        {
            return new LogUploadResult(0, 0, null, "An upload is already running.");
        }
        try
        {
            LogUploadResult result;
            if (!setup.SignedIn())
            {
                result = new LogUploadResult(0, 0, null, "Not signed in with Google, so there is no Drive to upload to.");
            }
            else
            {
                try
                {
                    var phone = setup.Phone ?? await ReadPhoneAsync(cancellationToken);
                    var folder = new DriveLogFolder(
                        SecretsVaultAccess.DriveFactory(setup.DriveToken, setup.Label),
                        mayCreateFolder: LogPhoneRole.MayCreateFolder(phone));
                    result = await new LogUploader(folder, Outbox, LoopInstall(phone))
                        .RunAsync(LivePaths, VaultPassphraseStore.Passphrase, cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    result = new LogUploadResult(0, 0, null, $"{e.GetType().Name}: {e.Message}");
                }
            }
            Record(result);
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref uploading, 0);
        }
    }

    /// <summary>Who is uploading: the app, its random install id, which phone, and which build.</summary>
    public LogInstall LoopInstall(string phone)
    {
        var preferences = Preferences();
        var id = preferences.GetString(InstallIdKey, null);
        if (string.IsNullOrEmpty(id))
        {
            id = LogInstall.NewInstallId();
            preferences.Edit()!.PutString(InstallIdKey, id)!.Apply();
        }
        var (version, code) = Version();
        return new LogInstall(setup.LoopApp, id, phone, version, code);
    }

    // An app on both phones is the owner's where this account owns the shared vault file, and hers where the
    // file was shared with it (the rule the apps already use). A vault that will not load says nothing.
    private async Task<string> ReadPhoneAsync(CancellationToken cancellationToken)
    {
        var loaded = await new SecretsVaultAccess(setup.DriveToken, setup.Label).Service.LoadAsync(cancellationToken);
        return LogPhoneRole.From(VaultOwnership.Decide(loaded.FileOwnership));
    }

    private void Record(LogUploadResult result)
    {
        var now = DateTimeOffset.Now;
        var history = History;
        history = result.Refusal is not null ? history : result.Succeeded ? history.Confirmed(now) : history.Failed(now);
        Preferences().Edit()!
            .PutString(HistoryKey, history.Format())!
            .PutBoolean(RefusedKey, result.Refusal is not null)!
            .Apply();

        if (result.Refusal is not null)
        {
            Info("LogUpload", LogUploadStatus.Sentence(result.Refusal, 0)!);
        }
        else if (result.Succeeded)
        {
            Info("LogUpload", $"Uploaded and confirmed {result.Confirmed} of {result.Planned} batches.");
        }
        else
        {
            Warn("LogUpload", $"Upload did not complete: {result.Error}");
        }
        if (result.Trashed > 0)
        {
            Info("LogUpload", $"Trashed {result.Trashed} processed files of this install.");
        }
        if (result.TrashError is not null)
        {
            Warn("LogUpload", $"Trashing processed files did not complete: {result.TrashError}");
        }
    }

    private void LogLaunch()
    {
        var (version, code) = Version();
        Info("Launch", LaunchLine.Format(setup.Label, version, code, Android.OS.Build.VERSION.Release ?? "?", (int)Android.OS.Build.VERSION.SdkInt));
    }

    private void Write(string level, string source, string message, Exception? exception)
    {
        try
        {
            Log.Append(DiagnosticLogFormatter.FromException(DateTimeOffset.Now, level, source, message, exception));
        }
        catch
        {
            // Diagnostics must never throw and mask the caller's own flow.
        }
    }

    private ISharedPreferences Preferences() =>
        app.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    private (string Name, long Code) Version()
    {
        try
        {
            var info = app.PackageManager?.GetPackageInfo(app.PackageName!, (PackageInfoFlags)0);
            return (info?.VersionName ?? "unknown", info?.LongVersionCode ?? 0);
        }
        catch
        {
            return ("unknown", 0);
        }
    }

    private static string ResultName<T>(T result)
    {
        var text = result?.ToString() ?? "null";
        var brace = text.IndexOf('{');
        return (brace > 0 ? text[..brace] : text).Trim();
    }
}

/// <summary>
/// A line for logcat and the head's file log at once (LOG-LOOP-PLAN.md §4.4): failure and decision paths that
/// only ever reached logcat — invisible after cutover — now also reach the weekly record. Debug chatter stays
/// on <c>Android.Util.Log</c>.
/// </summary>
public static class HeadLog
{
    public static void Warn(string tag, string message, Exception? exception = null)
    {
        Android.Util.Log.Warn(tag, exception is null ? message : $"{message}: {exception}");
        HeadDiagnostics.Current?.Warn(tag, message, exception);
    }

    public static void Info(string tag, string message)
    {
        Android.Util.Log.Info(tag, message);
        HeadDiagnostics.Current?.Info(tag, message);
    }
}

/// <summary>Turns a device-account sign-in that never came back into a NoReturn on the next screen resume.</summary>
internal sealed class SignInResumeCheck : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    public void OnActivityResumed(Activity activity) => VoiceGoogleAccount.CheckPendingSignIn(DateTimeOffset.Now, resumed: true);

    public void OnActivityCreated(Activity activity, Android.OS.Bundle? savedInstanceState)
    {
    }

    public void OnActivityDestroyed(Activity activity)
    {
    }

    public void OnActivityPaused(Activity activity)
    {
    }

    public void OnActivitySaveInstanceState(Activity activity, Android.OS.Bundle outState)
    {
    }

    public void OnActivityStarted(Activity activity)
    {
    }

    public void OnActivityStopped(Activity activity)
    {
    }
}
