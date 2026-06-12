using Android.Content;
using AndroidX.Work;

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

    private readonly ISharedPreferences preferences;

    public AccountStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public string? GmailAddress
    {
        get => preferences.GetString(AddressKey, null);
        set => preferences.Edit()!.PutString(AddressKey, value)!.Apply();
    }

    public string? GmailAppPassword
    {
        get => preferences.GetString(AppPasswordKey, null);
        set => preferences.Edit()!.PutString(AppPasswordKey, value)!.Apply();
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
            return Result.InvokeRetry();
        }
    }
}
