using Codex.Domain.Credentials;
using Android.Content;
using Codex.Domain.Post;
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
        var route = MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured);
        if (route == MailTransport.None)
        {
            return;
        }

        var preferences = context.GetSharedPreferences(SyncPreferences, FileCreationMode.Private)!;
        var now = DateTimeOffset.UtcNow;
        var lastSync = DateTimeOffset.TryParse(preferences.GetString(LastSyncKey, null), out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
        var since = EmailSyncWindow.Since(reconcile, lastSync, now, ReconcileWindow);

        var items = route == MailTransport.GmailApi
            ? await MailAccess.Api(context, account).FetchRecentAsync(since)
            : await FetchWithAppPasswordAsync(account, since);

        using var database = TimelineDatabase.Open(context);

        // The load-bearing line of the whole phase. Reconcile mirrors the source, and a
        // message the owner filed is still AT the source until its delete lands — so
        // without this it walks straight back into the Mailbox, over and over, and filing
        // looks broken.
        var tombstones = database.Filing.LoadTombstones();
        var visible = items.Where(item => !tombstones.IsFiled(item.SourceId)).ToList();

        if (reconcile)
        {
            database.Cache.ReconcileWindow(TimelineItemKind.Email, since, visible);
        }
        else
        {
            database.Cache.Upsert(visible);
        }

        await RetryOwedDeletesAsync(context, database, account, route, tombstones);

        // Sorting rides on the sync: the messages are already in hand and the phone is
        // already awake, so a second worker would only be a second thing to go wrong.
        await MailIntelligence.RunAsync(context, database, ReadingClient(context), now);

        // The archive is the one thing here that cannot be rebuilt from a source, so it is
        // the one thing worth carrying off the device. Written only when it has changed.
        if (VoiceGoogleAccount.IsSignedIn)
        {
            await new ArchiveBackup(MailAccess.Account().GetAccessToken, "Post").BackUpAsync(context);
        }
        preferences.Edit()!.PutString(LastSyncKey, now.ToString("O"))!.Apply();
    }

    // The model half of classification, or null when there is no key. Null is a working
    // state rather than an error: rules-only Post still sorts every bill and most machine
    // mail, and asks nothing of the network.
    private static MailReadingClient? ReadingClient(Context context)
    {
        try
        {
            var options = Codex.Ui.Voice.VoiceOptionsAccess.ForHead(
                MailAccess.Account().GetAccessToken, "Post");
            return new MailReadingClient(MailAccess.Shared, options);
        }
        catch (Exception e)
        {
            Android.Util.Log.Info("Post", $"No reading client: {e.Message}");
            return null;
        }
    }

    // A filing is not finished until the source agrees. Every sync retries what is still
    // owed; a delete that lands drops its tombstone, and one that keeps failing eventually
    // earns a fold — but the item stays filed either way, because the owner's intent stands
    // whatever the server does.
    private static async Task RetryOwedDeletesAsync(
        Context context, TimelineDatabase database, AccountStore account,
        MailTransport route, Tombstones tombstones)
    {
        var owed = tombstones.Outstanding();
        if (owed.Count == 0)
        {
            return;
        }

        var archive = database.Filing.LoadArchive();
        var byId = archive.All.ToDictionary(ghost => ghost.Item.SourceId, ghost => ghost.Item);

        foreach (var sourceId in owed)
        {
            if (!byId.TryGetValue(sourceId, out var item))
            {
                // Nothing left to delete with — the ghost is gone, so nothing is owed.
                tombstones.SourceConfirmed(sourceId);
                continue;
            }

            try
            {
                if (await DeleteAtSourceAsync(context, account, route, item))
                {
                    tombstones.SourceConfirmed(sourceId);
                }
                else
                {
                    tombstones.AttemptFailed(sourceId, DateTimeOffset.UtcNow);
                }
            }
            catch (Exception e)
            {
                tombstones.AttemptFailed(sourceId, DateTimeOffset.UtcNow);
                Android.Util.Log.Warn("Post", $"Owed delete of {sourceId} failed: {e}");
                PostDiagnostics.LogWarning("Filing", $"Owed delete of {sourceId} failed", e);
            }
        }

        database.Filing.SaveTombstones(tombstones);
    }

    private static async Task<bool> DeleteAtSourceAsync(
        Context context, AccountStore account, MailTransport route, TimelineItem item)
    {
        // Texts, calls and voicemail are local — the provider and the call log do not need a
        // round trip — but they were never actually deleted here despite the comment above once
        // claiming they were: Filing.File only ever suppressed them locally (Hidden.Hide), so the
        // row sat untouched in the provider/call log and a re-sync or an un-file could bring it
        // straight back. This is that missing delete, run from the retry loop rather than
        // synchronously at file-time so a missing permission or a disconnected bridge is retried
        // instead of silently dropped.
        if (item.Kind == TimelineItemKind.Call)
        {
            return DeleteCallAtSource(context, item);
        }
        if (item.Kind is TimelineItemKind.Message or TimelineItemKind.Voicemail)
        {
            return DeleteMessageAtSource(context, item);
        }
        if (item.MessageId is null)
        {
            return true; // nothing to address the delete to
        }

        return route switch
        {
            MailTransport.GmailApi => await MailAccess.Api(context, account).TrashAsync(item.MessageId),
            MailTransport.AppPassword => await DeleteWithAppPasswordAsync(account, item.MessageId),
            _ => false,
        };
    }

    private static bool DeleteCallAtSource(Context context, TimelineItem item)
    {
        if (!item.SourceId.All(char.IsAsciiDigit))
        {
            return true; // not a live call-log row; nothing to delete
        }
        try
        {
            var deleted = context.ContentResolver!.Delete(
                Android.Provider.CallLog.Calls.ContentUri!, "_id = ?", [item.SourceId]);
            return deleted > 0;
        }
        catch (Java.Lang.SecurityException)
        {
            return false; // WRITE_CALL_LOG not granted yet; retried on the next sync
        }
    }

    // The same routing MailboxArea's per-message delete (of the open correspondence) uses,
    // so filing and an outright delete agree on where a text actually lives.
    private static bool DeleteMessageAtSource(Context context, TimelineItem item)
    {
        var holdsSmsRole = HoldsSmsRole(context);
        switch (TextDelete.Choose(item.SourceId, holdsSmsRole, GmBridgeClient.Shared(context).IsConnected))
        {
            case TextDeletePath.SmsProvider:
                return DeleteProviderRow(context, Android.Provider.Telephony.Sms.ContentUri!, item, "sms:");
            case TextDeletePath.MmsProvider:
                return DeleteProviderRow(context, Android.Provider.Telephony.Mms.ContentUri!, item, "mms:");
            case TextDeletePath.Bridge:
                var messageId = item.SourceId["gm:".Length..];
                return !messageId.Contains(':') && GmBridgeClient.Shared(context).TryDelete(messageId);
            default:
                // Already suppressed locally by Filing.File's Hidden.Hide; nothing more owed.
                return true;
        }
    }

    private static bool DeleteProviderRow(Context context, Android.Net.Uri contentUri, TimelineItem item, string prefix)
    {
        try
        {
            var rowId = item.SourceId[prefix.Length..];
            var deleted = context.ContentResolver!.Delete(contentUri, "_id = ?", [rowId]);
            return deleted > 0;
        }
        catch (Java.Lang.SecurityException)
        {
            return false;
        }
    }

    private static bool HoldsSmsRole(Context context)
    {
        var roles = (Android.App.Roles.RoleManager?)context.GetSystemService(Context.RoleService);
        return roles is not null && roles.IsRoleAvailable(Android.App.Roles.RoleManager.RoleSms!)
            && roles.IsRoleHeld(Android.App.Roles.RoleManager.RoleSms!);
    }

    private static async Task<bool> DeleteWithAppPasswordAsync(AccountStore account, string messageId)
    {
        await new GmailClient(account.GmailAddress!, account.GmailAppPassword!).DeleteAsync(messageId);
        return true;
    }

    // The path Post is leaving. It stays only until the API is proven on a device: an
    // owner who has not yet registered Post as an OAuth client has no other way to reach
    // their mail, and shipping API-only would leave email dead in that window.
    private static async Task<IReadOnlyList<TimelineItem>> FetchWithAppPasswordAsync(
        AccountStore account, DateTimeOffset since)
    {
        var client = new GmailClient(account.GmailAddress!, account.GmailAppPassword!);
        var health = new CredentialHealthStore();
        try
        {
            var items = await client.FetchRecentAsync(since);
            // A completed authenticated fetch clears any prior app-password failure.
            health.MarkOk(CredentialIds.GmailAppPassword);
            return items;
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            // Only an auth rejection flags the app password; connect/network failures are transient.
            health.MarkFailing(CredentialIds.GmailAppPassword, ex.Message);
            throw;
        }
    }
}
