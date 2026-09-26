using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Downloads the MMS a WAP-push notification announced. Android hands the role holder only
/// the doorbell — <see cref="MmsDeliverReceiver"/> — fetching the message itself from the
/// carrier's MMSC and landing it in the system provider is this app's job, same as writing
/// an incoming text is (<see cref="SmsProviderWriter"/>).
///
/// The download runs over the phone's data connection and finishes asynchronously — it can
/// take seconds, and can fail (no data, MMSC unreachable, a malformed notification) — so
/// this only starts it. <see cref="MmsDownloadReceiver"/> is what the platform calls back
/// into once it settles, one way or the other.
/// </summary>
internal static class MmsDownloader
{
    // X-Mms-Message-Type value for a notification-ind, matching MmsNotificationParser's own
    // (that parser already validated the PDU actually was one before returning a result).
    private const int MessageTypeNotificationInd = 0x82;

    public static void RequestDownload(Context context, MmsNotification notification)
    {
        var manager = SmsManager.Default;
        if (manager is null)
        {
            PostDiagnostics.LogWarning("Mms", "This device has no SMS manager; cannot download the MMS.");
            return;
        }

        // Reserve a row for the message before asking the platform to fill it in — the
        // pattern the platform's own default-SMS-app documentation describes. Content-Location
        // is recorded too so a download that never completes at least confirms in DIAGNOSTICS
        // that something arrived, even though the row stays a bare notification-ind stub.
        var values = new ContentValues();
        values.Put(Telephony.IBaseMmsColumns.MessageType, MessageTypeNotificationInd);
        values.Put(Telephony.IBaseMmsColumns.TransactionId, notification.TransactionId);
        values.Put(Telephony.IBaseMmsColumns.ContentLocation, notification.ContentLocation);
        values.Put(Telephony.IBaseMmsColumns.MessageBox, SmsLogReader.MmsBoxInbox);
        values.Put(Telephony.IBaseMmsColumns.Read, 0);
        values.Put(Telephony.IBaseMmsColumns.Seen, 0);

        var uri = context.ContentResolver?.Insert(Telephony.Mms.ContentUri!, values);
        if (uri is null)
        {
            PostDiagnostics.LogWarning("Mms", "Could not reserve a provider row for an incoming MMS.");
            return;
        }

        // The platform writes the downloaded content back into this row, so it needs write
        // access to a Uri it did not create — holding the SMS role grants Post access to
        // content://mms itself, not automatically to the platform in the other direction.
        MmsAccess.GrantToPlatform(context, uri, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);

        // The row's own uri rides as the intent's Data rather than relying on whatever the
        // platform fills in on completion, so the receiver below always knows which row to
        // read regardless of what SmsManager's own callback contract does or doesn't add.
        var completed = new Intent(context, typeof(MmsDownloadReceiver)).SetData(uri);
        var pending = PendingIntent.GetBroadcast(
            context, notification.TransactionId.GetHashCode(), completed,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        manager.DownloadMultimediaMessage(context, notification.ContentLocation, uri, null, pending);
    }
}

/// <summary>
/// The platform's callback once a requested MMS download settles. A failure is left as a
/// bare notification-ind row (Post does not retry on its own — the carrier will re-deliver
/// the WAP push if it doesn't hear a response, per the MMS spec) rather than something that
/// needs its own retry logic here; a success just needs the owner told, since the row itself
/// is already sitting in the same provider <see cref="SmsLogReader"/> reads every reload.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.MmsDownloadReceiver",
    Exported = false)]
public class MmsDownloadReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        if (ResultCode != Result.Ok)
        {
            PostDiagnostics.LogWarning("Mms", $"MMS download did not complete (result {ResultCode}).");
            return;
        }

        try
        {
            var id = intent?.Data?.LastPathSegment;
            if (id is null)
            {
                return;
            }

            var address = SmsLogReader.ReadMmsAddress(context, id, SmsLogReader.MmsBoxInbox);
            var body = SmsLogReader.ReadMmsText(context, id)
                ?? AttachmentSummary.Describe(SmsLogReader.ReadMmsAttachments(context, id));
            SmsNotifications.NotifyIncoming(context, new IncomingSms(address, body));
        }
        catch (Exception e)
        {
            // Never throw out of a broadcast receiver: the download itself already
            // succeeded and the row is in the provider either way, so a notification
            // that fails to raise is a smaller loss than crashing the process.
            Android.Util.Log.Warn("Post", $"Could not notify on a downloaded MMS: {e}");
            PostDiagnostics.LogWarning("Mms", "Could not notify on a downloaded MMS", e);
        }
    }
}
