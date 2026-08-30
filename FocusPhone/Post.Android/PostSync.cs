using Android.Content;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Ui.Voice;

namespace Post;

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
public static class PostSync
{
    private const string SyncPreferences = "emailSync";
    private const string LastSyncKey = "lastSyncUtc";

    // How far back a reconcile mirrors. Matches the first-run fetch window;
    // messages older than this are outside the snapshot and left untouched.
    private static readonly TimeSpan ReconcileWindow = TimeSpan.FromDays(30);

    public static Task RunIncrementalAsync(Context context) => RunAsync(context, reconcile: false);

    public static Task RunReconcileAsync(Context context) => RunAsync(context, reconcile: true);

    private static async Task RunAsync(Context context, bool reconcile)
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
                PostDiagnostics.LogWarning("Bridge", "Bridge refresh during sync failed", e);
            }
        }

        var account = new AccountStore(context);
        if (!account.IsConfigured)
        {
            return;
        }

        var preferences = context.GetSharedPreferences(SyncPreferences, FileCreationMode.Private)!;
        var now = DateTimeOffset.UtcNow;
        var lastSync = DateTimeOffset.TryParse(preferences.GetString(LastSyncKey, null), out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
        var since = EmailSyncWindow.Since(reconcile, lastSync, now, ReconcileWindow);

        var client = new GmailClient(account.GmailAddress!, account.GmailAppPassword!);
        var health = new CredentialHealthStore();
        IReadOnlyList<TimelineItem> items;
        try
        {
            items = await client.FetchRecentAsync(since);
            // A completed authenticated fetch clears any prior app-password failure.
            health.MarkOk(CredentialIds.GmailAppPassword);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            // Only an auth rejection flags the app password; connect/network failures are transient.
            health.MarkFailing(CredentialIds.GmailAppPassword, ex.Message);
            throw;
        }

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
