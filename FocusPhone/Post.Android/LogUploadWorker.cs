using Android.Content;
using AndroidX.Work;
using Codex.Ui.Voice;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;

namespace Post;

/// <summary>
/// The weekly log upload (LOG-LOOP-PLAN.md §3.3, O12). Runs daily; each run asks
/// <see cref="UploadWindow.Due"/> whether this week's upload has happened yet — yes from Saturday 18:00 on
/// the phone's clock until one is confirmed — so a phone offline on Saturday uploads at its first
/// connection. The work itself is <see cref="PostLogUpload"/>, which Send logs now shares.
///
/// The registered Java name and the unique work name are persisted by WorkManager with
/// <c>ExistingPeriodicWorkPolicy.Keep</c>; moving either strands the schedule, so both are guarded.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.communication.LogUploadWorker")]
public class LogUploadWorker : Worker
{
    public const string WorkName = "log-upload";

    public LogUploadWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(LogUploadWorker), TimeSpan.FromDays(1))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public override Result DoWork() => PostDiagnostics.Worker(nameof(LogUploadWorker), Upload);

    private Result Upload()
    {
        if (!UploadWindow.Due(PostLogUpload.History(ApplicationContext).LastConfirmed, DateTimeOffset.Now))
        {
            return Result.InvokeSuccess();
        }
        // The worker bridges to the async upload at the true sync/async boundary, as EmailSyncWorker does.
        // A failed run is still a finished one: tomorrow's run is the retry (the outcome is recorded by
        // PostLogUpload), which keeps a phone with no network from waking its radio again and again.
        PostLogUpload.RunAsync(ApplicationContext, CancellationToken.None).GetAwaiter().GetResult();
        return Result.InvokeSuccess();
    }
}

/// <summary>
/// One upload run for Post: the live logs, the outbox, the log folder in the owner's Drive over the same
/// Google account the vault uses, and the vault passphrase from which the log key is derived. Records the
/// outcome for the window and the Diagnostics status sentence. Shared by the weekly worker and the
/// Send logs now button.
/// </summary>
internal static class PostLogUpload
{
    private const string PreferencesName = "log-loop";
    private const string HistoryKey = "uploadHistory";
    private const string RefusedKey = "uploadRefused";

    public static UploadHistory History(Context context) =>
        UploadHistory.Parse(Preferences(context).GetString(HistoryKey, null));

    /// <summary>The one sentence the Diagnostics screen shows about uploads, or null.</summary>
    public static string? Status(Context context)
    {
        var refused = Preferences(context).GetBoolean(RefusedKey, false) ? LogUploadRefusal.WaitingForPassphrase : (LogUploadRefusal?)null;
        return LogUploadStatus.Sentence(refused, History(context).FailedWeeks);
    }

    public static async Task<LogUploadResult> RunAsync(Context context, CancellationToken cancellationToken)
    {
        var outbox = PostDiagnostics.Outbox
            ?? throw new InvalidOperationException("Post's diagnostics were not installed, so there is no outbox to upload.");
        LogUploadResult result;
        if (!Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn)
        {
            result = new LogUploadResult(0, 0, null, "Not signed in with Google, so there is no Drive to upload to.");
        }
        else
        {
            var account = MailAccess.Account();
            var folder = new DriveLogFolder(SecretsVaultAccess.DriveFactory(account.GetAccessToken, PostDiagnostics.Label));
            try
            {
                result = await new LogUploader(folder, outbox, PostDiagnostics.LoopInstall(context))
                    .RunAsync(PostDiagnostics.LivePaths, VaultPassphraseStore.Passphrase, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                result = new LogUploadResult(0, 0, null, $"{e.GetType().Name}: {e.Message}");
            }
        }

        Record(context, result);
        return result;
    }

    private static void Record(Context context, LogUploadResult result)
    {
        var history = History(context);
        var now = DateTimeOffset.Now;
        history = result.Refusal is not null
            ? history
            : result.Succeeded ? history.Confirmed(now) : history.Failed(now);
        Preferences(context).Edit()!
            .PutString(HistoryKey, history.Format())!
            .PutBoolean(RefusedKey, result.Refusal is not null)!
            .Apply();

        if (result.Refusal is not null)
        {
            PostDiagnostics.LogInfo("LogUpload", LogUploadStatus.Sentence(result.Refusal, 0)!);
        }
        else if (result.Succeeded)
        {
            PostDiagnostics.LogInfo("LogUpload", $"Uploaded and confirmed {result.Confirmed} of {result.Planned} batches.");
        }
        else
        {
            PostDiagnostics.LogWarning("LogUpload", $"Upload did not complete: {result.Error}");
        }
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
