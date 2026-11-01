using Android.App;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>What a MAUI head tells the log loop about itself.</summary>
/// <param name="App">The loop's name for the app (index, folio, foreword): the batch header and the Drive file name.</param>
/// <param name="Label">The app's display name, for the launch line.</param>
/// <param name="SendLogsNowAction">The head's key-action id for Send logs now.</param>
/// <param name="Drive">A Drive service over the head's own Google account.</param>
/// <param name="Passphrase">The vault passphrase as the head stores it; the log key is derived from it.</param>
/// <param name="Phone">Which phone this is: <see cref="LogPhone.Owner"/> outright, or read from vault ownership.</param>
public sealed record MauiLoopSetup(
    string App,
    string Label,
    string SendLogsNowAction,
    Func<CancellationToken, Task<DriveService>> Drive,
    Func<string> Passphrase,
    Func<CancellationToken, Task<string>> Phone);

/// <summary>
/// The log loop for a MAUI head (LOG-LOOP-PLAN.md §3, §4): installed once after the app is built, beside the
/// <see cref="MauiLog"/> file log it carries. It writes the launch line and every process exit Android recorded since
/// the last launch, routes the tap watch's verdicts into the file log, and uploads the log to Drive — once the
/// Saturday window opens (<see cref="UploadIfDueAsync"/>, called from the head's own worker) or when the owner taps
/// Send logs now (<see cref="UploadAsync"/>).
/// </summary>
public static class MauiLogLoop
{
    private const string ExitLastSeenKey = "logLoop.exitLastSeen";
    private static int uploading;

    public static MauiLoopSetup? Setup { get; private set; }

    /// <summary>The one sentence for the Diagnostics screen about uploads, or none.</summary>
    public static string? Status => Run().Status;

    /// <summary>
    /// The phone an app on both phones is on: the owner's where this account owns the shared vault file, hers where
    /// it was shared with it. A vault that will not load answers unknown.
    /// </summary>
    public static Func<CancellationToken, Task<string>> PhoneFromVault(Func<Storyworthtelling.Voice.Secrets.SecretsVaultService> vault) =>
        async cancellationToken =>
        {
            var loaded = await vault().LoadAsync(cancellationToken).ConfigureAwait(false);
            return LogPhoneRole.From(Storyworthtelling.Voice.Secrets.VaultOwnership.Decide(loaded.FileOwnership));
        };

    public static void Install(MauiLoopSetup setup)
    {
        Setup = setup;
        LogLaunch(setup);
        RecordExits();
    }

    /// <summary>A tap-watch verdict, into the file log (plan §4.1).</summary>
    public static void Tap(TapVerdict verdict) =>
        MauiLog.Current?.Write("Tap", LogLevel.Warning, verdict.ToLogText(), null);

    /// <summary>A finished sign-in trail and its diagnosis (plan §4.2).</summary>
    public static void SignIn(SignInTrail trail) =>
        MauiLog.Current?.Write("SignIn", trail.Succeeded ? LogLevel.Information : LogLevel.Warning, trail.ToLogText(), null);

    /// <summary>A worker's start, end and result (plan §4.3); what it threw is logged and rethrown.</summary>
    public static T Worker<T>(string name, Func<T> body)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        MauiLog.Current?.Write("Worker", LogLevel.Information, WorkerTrail.StartedLine(name), null);
        try
        {
            var result = body();
            MauiLog.Current?.Write("Worker", LogLevel.Information, WorkerTrail.FinishedLine(name, ResultName(result), started.Elapsed), null);
            return result;
        }
        catch (Exception e)
        {
            MauiLog.Current?.Write("Worker", LogLevel.Warning, WorkerTrail.ThrewLine(name, started.Elapsed, e), e);
            throw;
        }
    }

    /// <summary>From the head's worker: uploads only inside the Saturday window, until a run is confirmed.</summary>
    public static async Task UploadIfDueAsync(CancellationToken cancellationToken)
    {
        if (Setup is not null && Run().Due)
        {
            await UploadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<LogUploadResult> UploadAsync(CancellationToken cancellationToken)
    {
        if (Setup is not { } setup || MauiLog.Current is not { } log)
        {
            return new LogUploadResult(0, 0, null, "The log loop is not installed in this process.");
        }
        if (Interlocked.CompareExchange(ref uploading, 1, 0) != 0)
        {
            return new LogUploadResult(0, 0, null, "An upload is already running.");
        }
        try
        {
            string phone;
            try
            {
                phone = await setup.Phone(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.Write("LogUpload", LogLevel.Warning, $"Could not tell which phone this is ({e.GetType().Name}); uploading as unknown.", null);
                phone = LogPhone.Unknown;
            }
            var folder = new DriveLogFolder(setup.Drive, mayCreateFolder: LogPhoneRole.MayCreateFolder(phone));
            var run = Run();
            var install = run.Install(setup.App, phone, AppInfo.Current.VersionString, long.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0);
            return await run.RunAsync(folder, log.Outbox, install, [log.LivePath], setup.Passphrase(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref uploading, 0);
        }
    }

    private static LogLoopRun Run() =>
        new(new PreferencesState(), (problem, line) => MauiLog.Current?.Write("LogUpload", problem ? LogLevel.Warning : LogLevel.Information, line, null));

    private static void LogLaunch(MauiLoopSetup setup) =>
        MauiLog.Current?.Write("Launch", LogLevel.Information, LaunchLine.Format(
            setup.Label, AppInfo.Current.VersionString, long.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0,
            Android.OS.Build.VERSION.Release ?? "?", (int)Android.OS.Build.VERSION.SdkInt), null);

    // G6: how the earlier processes ended, each logged once.
    private static void RecordExits()
    {
        try
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Android.Content.Context.ActivityService) is not ActivityManager manager || context.PackageName is null)
            {
                return;
            }
            var records = (manager.GetHistoricalProcessExitReasons(context.PackageName, 0, 0) ?? [])
                .Select(info => new ExitRecord(info.Reason, info.Status, info.Description, info.Timestamp, info.Importance))
                .ToList();
            var fresh = ExitReasonPhrases.NewSince(records, Preferences.Default.Get(ExitLastSeenKey, 0L));
            foreach (var record in fresh)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(record.Timestamp).ToLocalTime();
                MauiLog.Current?.Write("Exit", ExitReasonPhrases.IsProblem(record.Reason) ? LogLevel.Warning : LogLevel.Information,
                    $"EXIT at {when:yyyy-MM-dd HH:mm:ss zzz} — {ExitReasonPhrases.Describe(record)}", null);
            }
            if (fresh.Count > 0)
            {
                Preferences.Default.Set(ExitLastSeenKey, fresh[^1].Timestamp);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("ExitTrail", $"Could not read the exit reasons: {e.Message}");
        }
    }

    private static string ResultName<T>(T result)
    {
        var text = result?.ToString() ?? "null";
        var brace = text.IndexOf('{');
        return (brace > 0 ? text[..brace] : text).Trim();
    }

    private sealed class PreferencesState : ILogLoopState
    {
        public string? InstallId
        {
            get => Preferences.Default.Get<string?>("logLoop.installId", null);
            set => Preferences.Default.Set("logLoop.installId", value);
        }

        public string? UploadHistory
        {
            get => Preferences.Default.Get<string?>("logLoop.uploadHistory", null);
            set => Preferences.Default.Set("logLoop.uploadHistory", value);
        }

        public bool WaitingForPassphrase
        {
            get => Preferences.Default.Get("logLoop.waitingForPassphrase", false);
            set => Preferences.Default.Set("logLoop.waitingForPassphrase", value);
        }
    }
}
