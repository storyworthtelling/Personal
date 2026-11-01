using Storyworthtelling.Diagnostics;
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

    internal const string ExtraFile = "com.storyworthtelling.focusphone.communication.extra.MMS_SENT_FILE";

    // A send's key action by transaction id until the platform answers; in memory only, like SmsSender's.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Storyworthtelling.Diagnostics.KeyAction> Pending = new();

    internal const string ExtraTransaction = "com.storyworthtelling.focusphone.communication.extra.MMS_SENT_TRANSACTION";

    /// <summary>The platform answered a send: report it to the key action that started it, if still here.</summary>
    internal static void Answered(string? transactionId, int resultCode)
    {
        if (transactionId is null || !Pending.TryRemove(transactionId, out var action))
        {
            return;
        }
        if (resultCode == PostLogLines.ResultOk)
        {
            action.Succeeded();
        }
        else
        {
            action.Failed(PostLogLines.MmsSent(resultCode));
        }
    }

    public static void Send(
        Context context, IReadOnlyList<string> recipients, string? text, IReadOnlyList<MmsOutboundPart> media,
        Storyworthtelling.Diagnostics.KeyAction action)
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

        // The file's own path rides along: the Uri is Post's FileProvider address for the
        // platform, and its Path is "/mms-outbox/…", not somewhere on disk.
        var completed = new Intent(context, typeof(MmsSendReceiver))
            .SetData(uri)
            .PutExtra(ExtraFile, file.AbsolutePath)
            .PutExtra(ExtraTransaction, transactionId);
        Pending[transactionId] = action;
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

        var path = intent?.GetStringExtra(MmsSender.ExtraFile);
        try
        {
            var code = (int)ResultCode;
            MmsSender.Answered(intent?.GetStringExtra(MmsSender.ExtraTransaction), code);
            if (ResultCode == Result.Ok)
            {
                PostDiagnostics.LogInfo("Mms", PostLogLines.MmsSent(code));
            }
            if (ResultCode != Result.Ok)
            {
                PostDiagnostics.LogWarning("Mms", PostLogLines.MmsSent(code));
                PendingMmsSendFailureStore.MarkFailed(context);
                return;
            }

            if (path is null || !System.IO.File.Exists(path))
            {
                PostDiagnostics.LogWarning("Mms", "A sent MMS's file was gone before it could be recorded.");
                return;
            }
            var pdu = System.IO.File.ReadAllBytes(path);
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
            if (path is not null)
            {
                System.IO.File.Delete(path);
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

        // Parts before addresses, as for a received message: a reload mid-write then sees a
        // message with nobody on it, which the reader skips, not one with half its content.
        foreach (var part in info.Parts)
        {
            var isText = part.MimeType == "text/plain";
            MmsProviderWriter.InsertPart(context, id, new MmsPartRow(
                part.MimeType, 106, part.FileName, part.FileName, null,
                isText ? System.Text.Encoding.UTF8.GetString(part.Data) : null,
                isText ? null : part.Data));
        }
        foreach (var recipient in info.Recipients)
        {
            MmsProviderWriter.InsertAddress(context, id, recipient, MmsInboxRecord.AddressTo);
        }
    }

    private const int MmsBoxSent = 2;

    private static long GetOrCreateThreadId(Context context, IReadOnlyList<string> recipients) =>
        Telephony.Threads.GetOrCreateThreadId(context, recipients.ToList());
}
