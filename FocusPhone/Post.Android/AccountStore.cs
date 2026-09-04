using Android.Content;
using AndroidX.Work;
using Codex.Infrastructure.Email;

namespace Post;

/// <summary>
/// Gmail account settings. The app password is stored in app-private
/// preferences on a device-owner-locked phone; rotation happens in the
/// launcher's admin mode by re-opening this app's settings screen.
/// </summary>
public class AccountStore
{
    private const string PreferencesName = "account";
    private const string AddressKey = "gmailAddress";
    private const string AppPasswordKey = "gmailAppPassword";
    private const string GmBridgeSessionKey = "gmBridgeSession";
    private const string SmsRoleHeldKey = "smsRoleHeld";

    private readonly ISharedPreferences preferences;

    public AccountStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public string? GmailAddress
    {
        get => preferences.GetString(AddressKey, null);
        // Store the normalised address so the value the screen shows back and the
        // value the sync worker authenticates with are the same clean string.
        set => preferences.Edit()!.PutString(AddressKey, GmailCredentials.NormalizeAddress(value))!.Apply();
    }

    public string? GmailAppPassword
    {
        get => preferences.GetString(AppPasswordKey, null);
        // Strip Google's display spaces before persisting, otherwise a
        // pasted-verbatim app password fails auth every sync.
        set => preferences.Edit()!.PutString(AppPasswordKey, GmailCredentials.NormalizeAppPassword(value))!.Apply();
    }

    /// <summary>The gmbridge (Messages-for-Web) session blob; present once paired.</summary>
    public string? GmBridgeSession
    {
        get => preferences.GetString(GmBridgeSessionKey, null);
        set => preferences.Edit()!.PutString(GmBridgeSessionKey, value)!.Apply();
    }

    /// <summary>
    /// Whether Post held the SMS role the last time it looked. Kept because the useful
    /// question on launch is not "do we hold it" but "did we just lose it" — the same
    /// missing role reads as first-run setup or as something silently breaking texting,
    /// and those want different words.
    /// </summary>
    public bool SmsRoleHeldAtLastLaunch
    {
        get => preferences.GetBoolean(SmsRoleHeldKey, false);
        set => preferences.Edit()!.PutBoolean(SmsRoleHeldKey, value)!.Apply();
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(GmailAddress) && !string.IsNullOrEmpty(GmailAppPassword);
}

/// <summary>
/// Periodic Gmail sync into the timeline cache (last 30 days on first run,
/// then incremental). The one-shot run after configuring the account
/// reconciles the full window so deletions land immediately; the actual sync
/// work lives in <see cref="PostSync"/>, shared with the Sync button.
///
/// The JNI class name is REGISTERED under the pre-rename namespace and frozen: the
/// periodic request is persisted by class name in WorkManager's own database, and it
/// is enqueued with ExistingPeriodicWorkPolicy.Keep — so on an upgraded device the
/// stale registration wins, and if the class it names has moved the email sync simply
/// never runs again, with nothing to see.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.communication.EmailSyncWorker")]
public class EmailSyncWorker : Worker
{
    private const string WorkName = "focusphone-email-sync";
    private const string ReconcileKey = "reconcile";

    public EmailSyncWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(EmailSyncWorker), TimeSpan.FromMinutes(30))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    /// <summary>
    /// Runs a single reconciling sync now (e.g. right after the account is
    /// configured) so the timeline populates promptly — and drops anything
    /// already deleted server-side — instead of waiting for the next period.
    /// </summary>
    public static void RunOnce(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new OneTimeWorkRequest.Builder(typeof(EmailSyncWorker))
            .SetConstraints(constraints)!
            .SetInputData(new Data.Builder().PutBoolean(ReconcileKey, true)!.Build()!)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork(
            "focusphone-email-sync-once", ExistingWorkPolicy.Replace!, (OneTimeWorkRequest)request);
    }

    public override Result DoWork()
    {
        try
        {
            // A WorkManager Worker.DoWork runs synchronously on its own background executor thread, so
            // bridging the async sync here (rather than inside PostSync) keeps the blocking at the
            // true sync/async boundary while the UI's Sync button awaits it properly.
            if (InputData.GetBoolean(ReconcileKey, false))
            {
                PostSync.RunReconcileAsync(ApplicationContext).GetAwaiter().GetResult();
            }
            else
            {
                PostSync.RunIncrementalAsync(ApplicationContext).GetAwaiter().GetResult();
            }
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneEmail", $"Email sync failed; will retry: {e}");
            // This runs invisibly in the background worker; without a durable record
            // a failing sync would silently stop conversations updating.
            PostDiagnostics.LogWarning("EmailSync", "Email sync failed; will retry", e);
            return Result.InvokeRetry();
        }
    }
}
