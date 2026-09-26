using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using Codex.Domain.Post;
using System.Linq;

namespace Post;

/// <summary>
/// Sends an MMS — one or more recipients, optional text, optional photos/videos — as the
/// role holder. The one thing this exists to fix: a reply to a group text used to have
/// nowhere to go at all (<see cref="MailboxArea"/>'s action row refuses group threads),
/// because sending one recipient at a time is not a group reply, it is N separate texts that
/// each recipient sees fragmented from everyone else's.
///
/// <see cref="MmsComposer"/> builds the wire PDU; this writes it to a file the platform's MMS
/// transport can read, asks it to send, and — once <see cref="MmsSendReceiver"/> hears back —
/// writes the same message into the local record as a Sent row, exactly as
/// <see cref="SmsSender"/> does for a plain text. The recipients/text/media are not
/// remembered by this class between the send call and the completion callback: the completion
/// receiver reads them back out of the PDU file itself (<see cref="MmsSendRequestParser"/>),
/// because an in-memory handoff would be lost if Android reclaimed the process while the send
/// was still in flight over the network — the file survives that; a static field would not.
/// </summary>
internal static class MmsSender
{
    private const string OutboxFolder = "mms-outbox";

    public static void Send(
        Context context, IReadOnlyList<string> recipients, string? text, IReadOnlyList<MmsOutboundPart> media)
    {
        var manager = SmsManager.Default
            ?? throw new InvalidOperationException("This device has no SMS manager.");

        var transactionId = Guid.NewGuid().ToString("N");
        var pdu = MmsComposer.ComposeSendRequest(
            transactionId, recipients, subject: null, text, media, DateTimeOffset.UtcNow);

        var directory = new Java.IO.File(context.CacheDir, OutboxFolder);
        directory.Mkdirs();
        var file = new Java.IO.File(directory, $"{transactionId}.dat");
        System.IO.File.WriteAllBytes(file.AbsolutePath!, pdu);

        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context, $"{context.PackageName}.attachments", file);

        // The platform's MMS transport reads this Uri to transmit it; it is Post's own
        // FileProvider file, not a system content provider Post already has standing access
        // to, so it needs its own grant (mirrors MmsDownloader's write-side grant).
        MmsAccess.GrantToPlatform(context, uri, ActivityFlags.GrantReadUriPermission);

        var completed = new Intent(context, typeof(MmsSendReceiver)).SetData(uri);
        var pending = PendingIntent.GetBroadcast(
            context, transactionId.GetHashCode(), completed,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        manager.SendMultimediaMessage(context, uri, null, null, pending);
    }
}

/// <summary>
/// The platform's callback once a requested MMS send settles. Success writes the local
/// "sent" record (thread id resolved by Android's own conversation lookup, so it lands in
/// the same thread the group's incoming messages already thread under — not a new one keyed
/// slightly differently); failure is recorded for the Mailbox to surface on next open,
/// mirroring the call-note pattern (<see cref="PendingCallNoteStore"/>) rather than the
/// pinned bottom bar, which is already gone by the time this fires.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.MmsSendReceiver",
    Exported = false)]
public class MmsSendReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        var uri = intent?.Data;
        try
        {
            if (ResultCode != Result.Ok)
            {
                PostDiagnostics.LogWarning("Mms", $"MMS send did not complete (result {ResultCode}).");
                PendingMmsSendFailureStore.MarkFailed(context);
                return;
            }

            if (uri?.Path is null)
            {
                return;
            }
            var pdu = System.IO.File.ReadAllBytes(uri.Path);
            var info = MmsSendRequestParser.Parse(pdu);
            if (info is null)
            {
                PostDiagnostics.LogWarning("Mms", "A sent MMS could not be read back for the local record.");
                return;
            }

            RecordSent(context, info);
        }
        catch (Exception e)
        {
            // Never throw out of a broadcast receiver: the send itself already went out (or
            // definitively failed) over the network either way, so a local record that fails
            // to write is a smaller loss than crashing the process.
            Android.Util.Log.Warn("Post", $"Could not record a sent MMS: {e}");
            PostDiagnostics.LogWarning("Mms", "Could not record a sent MMS", e);
        }
        finally
        {
            if (uri?.Path is not null)
            {
                System.IO.File.Delete(uri.Path);
            }
        }
    }

    // Writing the sent record: a base row, an addr row per recipient, and a part per
    // attachment (or one text/plain part) — the same three tables SmsLogReader already
    // reads, so the message shows up in the thread on the very next reload.
    private static void RecordSent(Context context, MmsSendRequestInfo info)
    {
        var threadId = GetOrCreateThreadId(context, info.Recipients);

        var values = new ContentValues();
        values.Put(Telephony.IBaseMmsColumns.MessageBox, MmsBoxSent);
        values.Put(Telephony.IBaseMmsColumns.ThreadId, threadId);
        values.Put(Telephony.IBaseMmsColumns.Date, Java.Lang.JavaSystem.CurrentTimeMillis() / 1000);
        values.Put(Telephony.IBaseMmsColumns.Read, 1);
        values.Put(Telephony.IBaseMmsColumns.Seen, 1);
        if (!string.IsNullOrEmpty(info.Subject))
        {
            values.Put(Telephony.IBaseMmsColumns.Subject, info.Subject);
        }

        var messageUri = context.ContentResolver?.Insert(Telephony.Mms.ContentUri!, values);
        if (messageUri is null)
        {
            return;
        }
        var id = messageUri.LastPathSegment;
        if (id is null)
        {
            return;
        }

        foreach (var recipient in info.Recipients)
        {
            var addrValues = new ContentValues();
            addrValues.Put("address", recipient);
            addrValues.Put("type", MmsAddrTo);
            addrValues.Put("charset", 106); // UTF-8, matching MmsComposer's own EncodedStringValue charset.
            context.ContentResolver?.Insert(Android.Net.Uri.Parse($"content://mms/{id}/addr")!, addrValues);
        }

        foreach (var part in info.Parts)
        {
            InsertPart(context, id, part);
        }
    }

    private static void InsertPart(Context context, string messageId, MmsOutboundPart part)
    {
        var values = new ContentValues();
        values.Put("mid", messageId);
        values.Put("ct", part.MimeType);
        values.Put("fn", part.FileName);
        if (part.MimeType == "text/plain")
        {
            // A short text part can live directly in the row's own "text" column — the same
            // column SmsLogReader.ReadMmsText already reads, no stream write needed.
            values.Put("text", System.Text.Encoding.UTF8.GetString(part.Data));
            context.ContentResolver?.Insert(Android.Net.Uri.Parse("content://mms/part")!, values);
            return;
        }

        var partUri = context.ContentResolver?.Insert(Android.Net.Uri.Parse("content://mms/part")!, values);
        if (partUri is null)
        {
            return;
        }
        using var stream = context.ContentResolver?.OpenOutputStream(partUri);
        stream?.Write(part.Data, 0, part.Data.Length);
    }

    // Telephony.Mms.Addr type value for "to" — the same constant SmsLogReader keys off.
    private const string MmsAddrTo = "151";
    private const int MmsBoxSent = 2;

    private static long GetOrCreateThreadId(Context context, IReadOnlyList<string> recipients) =>
        Telephony.Threads.GetOrCreateThreadId(context, recipients.ToList());
}
