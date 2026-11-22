using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Ui.Voice;

namespace Post;

/// <summary>
/// The undo-first delete, carried out (DESIGN.md §7), shared by the Mailbox and Dash's listener (P14 B2) so a message
/// deleted on the phone or on the wrist is held in one place and deleted at the source by one piece of code.
///
/// <see cref="Held"/> is the process's one delete buffer: an item scheduled in it is gone from every view that filters by
/// it at once (the Mailbox's list and its open correspondence, and what the wrist is shown), and stays gone until its
/// window closes and <see cref="Commit"/> runs, or an undo lets it go. <see cref="Commit"/> is the irreversible,
/// channel-specific delete: a mail to Gmail's trash, a call or voicemail off the call log, a text through the provider or
/// Google Messages, as <see cref="TextDelete"/> decides.
/// </summary>
internal static class SourceDeletes
{
    /// <summary>The deletes waiting out their undo window in this process, from the phone and the wrist alike.</summary>
    public static DeleteBuffer Held { get; } = new();

    /// <summary>Performs the irreversible, channel-specific delete once the undo window closes. Off the calling thread.</summary>
    public static void Commit(Context context, TimelineItem item)
    {
        var app = context.ApplicationContext ?? context;
        Task.Run(() =>
        {
            try
            {
                switch (item.Kind)
                {
                    case TimelineItemKind.Email:
                        DeleteEmail(app, item);
                        break;
                    case TimelineItemKind.Call:
                    case TimelineItemKind.Voicemail:
                        DeleteCallOrVoicemail(app, item);
                        break;
                    default:
                        DeleteText(app, item);
                        break;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Delete of a {item.Kind} failed: {e.GetType().Name}");
                PostDiagnostics.LogWarning("Delete", $"Delete of a {item.Kind} failed", e);
            }
        });
    }

    private static void DeleteEmail(Context context, TimelineItem item)
    {
        var account = new AccountStore(context);
        if (item.MessageId is not null)
        {
            switch (MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured))
            {
                case MailTransport.GmailApi:
                    // A real trash, recoverable for 30 days — the same delete the Gmail app
                    // performs, rather than IMAP's move into a folder that happens to be named
                    // Trash. This is what makes filing safe enough to be a first-class verb.
                    MailAccess.Api(context, account).TrashAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
                case MailTransport.AppPassword:
                    new GmailClient(account.GmailAddress!, account.GmailAppPassword!)
                        .DeleteAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
            }
        }
        using var database = TimelineDatabase.Open(context);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    // Deleting a text for real depends on where it came from, and the routing is decided
    // by pure tested logic rather than here. A bridge message is deleted in Google Messages;
    // a provider row only while Post itself holds the texting role.
    private static void DeleteText(Context context, TimelineItem item)
    {
        var holdsRole = SmsRoleAccess.Status(context) == SmsRoleStatus.Held;
        switch (TextDelete.Choose(item.SourceId, holdsRole, GmBridgeClient.Shared(context).IsConnected))
        {
            case TextDeletePath.SmsProvider:
                DeleteProviderRow(context, Android.Provider.Telephony.Sms.ContentUri!, item, "sms:");
                return;
            case TextDeletePath.MmsProvider:
                DeleteProviderRow(context, Android.Provider.Telephony.Mms.ContentUri!, item, "mms:");
                return;
            case TextDeletePath.Bridge:
                // Off the UI thread (it is a network call); gone from Post either way at once.
                var messageId = item.SourceId["gm:".Length..];
                HideLocally(context, item);
                var bridge = GmBridgeClient.Shared(context);
                Task.Run(() =>
                {
                    if (!messageId.Contains(':') && bridge.TryDelete(messageId) != BridgeDeleteResult.Deleted)
                    {
                        PostDiagnostics.LogWarning("Bridge", "A message was hidden here but could not be deleted in Google Messages");
                    }
                });
                return;
        }

        HideLocally(context, item);
    }

    // Removes the row the timeline item came from. The provider is the phone's own record
    // of the owner's texts, so this is the delete that actually means something — every
    // other app that reads it sees the message gone too.
    private static void DeleteProviderRow(Context context, Android.Net.Uri contentUri, TimelineItem item, string prefix)
    {
        var rowId = item.SourceId[prefix.Length..];
        context.ContentResolver!.Delete(contentUri, "_id = ?", [rowId]);
        using var database = TimelineDatabase.Open(context);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private static void DeleteCallOrVoicemail(Context context, TimelineItem item)
    {
        if (item.SourceId.All(char.IsAsciiDigit))
        {
            // A live call-log row: remove it at the source (needs WRITE_CALL_LOG).
            // "_id" is the stable BaseColumns row-id name for the call-log provider.
            context.ContentResolver!.Delete(Android.Provider.CallLog.Calls.ContentUri!,
                "_id = ?", [item.SourceId]);
        }
        using var database = TimelineDatabase.Open(context);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private static void HideLocally(Context context, TimelineItem item)
    {
        using var database = TimelineDatabase.Open(context);
        database.Hidden.Hide(item.Kind, item.SourceId);
        database.Cache.Delete(item.Kind, item.SourceId);
    }
}
