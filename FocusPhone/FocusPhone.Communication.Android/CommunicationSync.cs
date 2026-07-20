using Android.Content;
using FocusPhone.Domain.Timeline;
using FocusPhone.Infrastructure.Email;

namespace FocusPhone.Communication;

/// <summary>
/// Drives a Gmail sync into the timeline cache. Two modes share one path so the
/// periodic worker and the Sync button behave identically:
///  • Incremental — fetch only messages newer than the last successful sync and
///    upsert them. Cheap; used by the 30-minute periodic worker.
///  • Reconcile — fetch the whole recent window and mirror it, so messages the
///    owner deleted (or archived out of the inbox) in Gmail drop out of the cache
///    instead of lingering and cluttering the list. Used by the Sync button and
///    right after the account is configured.
/// The gmbridge text stream is a push feed with no delete channel, so the most a
/// sync can do for texts is make sure the bridge is connected and draining.
/// </summary>
public static class CommunicationSync
{
    private const string SyncPreferences = "emailSync";
    private const string LastSyncKey = "lastSyncUtc";

    // How far back a reconcile mirrors. Matches the first-run fetch window;
    // messages older than this are outside the snapshot and left untouched.
    private static readonly TimeSpan ReconcileWindow = TimeSpan.FromDays(30);

    public static void RunIncremental(Context context) => Run(context, reconcile: false);

    public static void RunReconcile(Context context) => Run(context, reconcile: true);

    private static void Run(Context context, bool reconcile)
    {
        // A reconcile is the owner asking to be brought fully up to date, so make
        // sure the bridge is online and draining current texts. The periodic
        // incremental run leaves the bridge to the app's own startup connect.
        if (reconcile)
        {
            try
            {
                GmBridgeClient.Shared(context).ConnectIfPaired();
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Bridge refresh during sync failed: {e}");
                CommunicationDiagnostics.LogWarning("Bridge", "Bridge refresh during sync failed", e);
            }
        }

        var account = new AccountStore(context);
        if (!account.IsConfigured)
        {
            return;
        }

        var preferences = context.GetSharedPreferences(SyncPreferences, FileCreationMode.Private)!;
        var now = DateTimeOffset.UtcNow;
        // A reconcile always fetches the full window so the snapshot is complete;
        // an incremental sync resumes from the last success (falling back to the
        // window on first run).
        var since = reconcile
            ? now - ReconcileWindow
            : DateTimeOffset.TryParse(preferences.GetString(LastSyncKey, null), out var parsed)
                ? parsed
                : now - ReconcileWindow;

        var client = new GmailClient(account.GmailAddress!, account.GmailAppPassword!);
        var items = client.FetchRecentAsync(since).GetAwaiter().GetResult();

        using var database = TimelineDatabase.Open(context);
        if (reconcile)
        {
            database.Cache.ReconcileWindow(TimelineItemKind.Email, since, items);
        }
        else
        {
            database.Cache.Upsert(items);
        }
        preferences.Edit()!.PutString(LastSyncKey, now.ToString("O"))!.Apply();
    }
}
