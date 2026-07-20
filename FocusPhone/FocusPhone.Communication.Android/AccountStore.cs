using Android.Content;
using AndroidX.Work;
using FocusPhone.Infrastructure.Email;

namespace FocusPhone.Communication;

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

    public bool IsConfigured =>
        !string.IsNullOrEmpty(GmailAddress) && !string.IsNullOrEmpty(GmailAppPassword);
}

/// <summary>
/// Periodic Gmail sync into the timeline cache (last 30 days on first run,
/// then incremental). IMAP IDLE push can replace polling later; the worker is
/// the reliable baseline.
/// </summary>
public class EmailSyncWorker : Worker
{
    private const string WorkName = "focusphone-email-sync";
    private const string SyncPreferences = "emailSync";
    private const string LastSyncKey = "lastSyncUtc";

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
    /// Runs a single sync now (e.g. right after the account is configured) so
    /// the timeline populates promptly instead of waiting for the next period.
    /// </summary>
    public static void RunOnce(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new OneTimeWorkRequest.Builder(typeof(EmailSyncWorker))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork(
            "focusphone-email-sync-once", ExistingWorkPolicy.Replace!, (OneTimeWorkRequest)request);
    }

    public override Result DoWork()
    {
        var account = new AccountStore(ApplicationContext);
        if (!account.IsConfigured)
        {
            return Result.InvokeSuccess();
        }

        var preferences = ApplicationContext.GetSharedPreferences(SyncPreferences, FileCreationMode.Private)!;
        var since = DateTimeOffset.TryParse(preferences.GetString(LastSyncKey, null), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow.AddDays(-30);

        try
        {
            var client = new Infrastructure.Email.GmailClient(
                account.GmailAddress!, account.GmailAppPassword!);
            var items = client.FetchRecentAsync(since).GetAwaiter().GetResult();
            using var database = TimelineDatabase.Open(ApplicationContext);
            database.Cache.Upsert(items);
            preferences.Edit()!.PutString(LastSyncKey, DateTimeOffset.UtcNow.ToString("O"))!.Apply();
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneEmail", $"Email sync failed; will retry: {e}");
            // This runs invisibly in the background worker; without a durable record
            // a failing sync would silently stop conversations updating.
            CommunicationDiagnostics.LogWarning("EmailSync", "Email sync failed; will retry", e);
            return Result.InvokeRetry();
        }
    }
}
