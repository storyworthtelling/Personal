using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Downloads the MMS a WAP-push notification announced. Android hands the role holder only
/// the doorbell — <see cref="MmsDeliverReceiver"/> — and then, once asked, the downloaded
/// message as raw bytes: the platform's MMS service writes the PDU to the Uri it is given and
/// stores nothing itself for the default SMS app (AOSP DownloadRequest.persistIfRequired
/// returns early unless auto-persisting is on, and it is off by default). So the message is
/// downloaded into a file, and <see cref="MmsDownloadReceiver"/> reads it and writes it into
/// the provider.
///
/// This used to hand the platform a provider message row as the destination. The MMS
/// provider opens a file only for a part, never for a message, so every download failed to
/// write and left an empty placeholder: no group text, no picture, ever reached the Mailbox.
///
/// The placeholder row is still written first. It is the record that something was
/// announced, and it carries what a retry needs (<see cref="MmsStubSweep"/>), since the
/// carrier announces a message once and keeps it until it expires.
/// </summary>
internal static class MmsDownloader
{
    private const string InboxFolder = "mms-inbox";

    internal const string ExtraFile = "com.storyworthtelling.focusphone.communication.extra.MMS_FILE";
    internal const string ExtraStub = "com.storyworthtelling.focusphone.communication.extra.MMS_STUB";
    internal const string ExtraTransactionId = "com.storyworthtelling.focusphone.communication.extra.MMS_TRANSACTION";
    internal const string ExtraContentLocation = "com.storyworthtelling.focusphone.communication.extra.MMS_LOCATION";

    // X-Mms-Message-Type value for a notification-ind, matching MmsNotificationParser's own
    // (that parser already validated the PDU actually was one before returning a result).
    private const int MessageTypeNotificationInd = 0x82;

    public static void RequestDownload(Context context, MmsNotification notification)
    {
        // A carrier that hears no acknowledgement may announce the same message again; the
        // placeholder already there is reused rather than doubled.
        var stub = FindStub(context, notification.TransactionId) ?? ReserveStub(context, notification);
        if (stub is null)
        {
            PostDiagnostics.LogWarning("Mms", "Could not reserve a provider row for an incoming MMS.");
            return;
        }
        Start(context, stub, notification.TransactionId, notification.ContentLocation);
    }

    /// <summary>Asks the platform to fetch a message into a fresh file; the receiver does the rest.</summary>
    internal static void Start(Context context, Android.Net.Uri stub, string transactionId, string contentLocation)
    {
        var manager = SmsManager.Default;
        if (manager is null)
        {
            PostDiagnostics.LogWarning("Mms", "This device has no SMS manager; cannot download the MMS.");
            return;
        }

        var directory = new Java.IO.File(context.CacheDir, InboxFolder);
        directory.Mkdirs();
        var file = new Java.IO.File(directory, $"{Guid.NewGuid():N}.pdu");
        file.CreateNewFile();
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context, $"{context.PackageName}.attachments", file);

        // The platform's MMS transport writes the downloaded PDU into Post's own file, which
        // it can only do with an explicit grant.
        MmsAccess.GrantToPlatform(context, uri, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);

        // Everything the receiver needs rides in the intent, so a download that finishes
        // after Android has reclaimed the process still lands.
        var completed = new Intent(context, typeof(MmsDownloadReceiver))
            .SetData(uri)
            .PutExtra(ExtraFile, file.AbsolutePath)
            .PutExtra(ExtraStub, stub.ToString())
            .PutExtra(ExtraTransactionId, transactionId)
            .PutExtra(ExtraContentLocation, contentLocation);
        var pending = PendingIntent.GetBroadcast(
            context, file.AbsolutePath!.GetHashCode(), completed,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        manager.DownloadMultimediaMessage(context, contentLocation, uri, null, pending);
    }

    private static Android.Net.Uri? ReserveStub(Context context, MmsNotification notification)
    {
        var values = new ContentValues();
        values.Put(Telephony.IBaseMmsColumns.MessageType, MessageTypeNotificationInd);
        values.Put(Telephony.IBaseMmsColumns.TransactionId, notification.TransactionId);
        values.Put(Telephony.IBaseMmsColumns.ContentLocation, notification.ContentLocation);
        values.Put(Telephony.IBaseMmsColumns.MessageBox, SmsLogReader.MmsBoxInbox);
        values.Put(Telephony.IBaseMmsColumns.Read, 0);
        values.Put(Telephony.IBaseMmsColumns.Seen, 0);
        return context.ContentResolver?.Insert(Telephony.Mms.ContentUri!, values);
    }

    private static Android.Net.Uri? FindStub(Context context, string transactionId)
    {
        using var cursor = context.ContentResolver?.Query(
            Telephony.Mms.ContentUri!, ["_id"], "m_type = ? AND tr_id = ?",
            [MessageTypeNotificationInd.ToString(), transactionId], null);
        return cursor is not null && cursor.MoveToFirst()
            ? Android.Net.Uri.Parse($"content://mms/{cursor.GetLong(0)}")
            : null;
    }
}

/// <summary>
/// The platform's callback once a requested download settles. On success it reads the
/// downloaded PDU (<see cref="MmsRetrieveConfParser"/>), writes the message into the provider
/// in the thread Android keeps for its sender and recipients (<see cref="MmsProviderWriter"/>),
/// drops the placeholder, tells the carrier it arrived, and raises the notification. On
/// failure it leaves the placeholder for <see cref="MmsStubSweep"/> to try again.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.MmsDownloadReceiver",
    Exported = false)]
public class MmsDownloadReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        // Reading a photo-sized file and writing it into the provider is more than a receiver
        // should do on the main thread; GoAsync keeps the process alive until it is done.
        var resultCode = ResultCode;
        var pending = GoAsync();
        var app = context.ApplicationContext ?? context;
        Task.Run(() =>
        {
            try
            {
                Land(app, intent, resultCode);
            }
            catch (Exception e)
            {
                // Never let it escape: the placeholder is still there for the sweep to retry.
                Android.Util.Log.Warn("Post", $"Could not store a downloaded MMS: {e}");
                PostDiagnostics.LogWarning("Mms", "Could not store a downloaded MMS", e);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    private static void Land(Context context, Intent intent, Result resultCode)
    {
        var path = intent.GetStringExtra(MmsDownloader.ExtraFile);
        var stub = intent.GetStringExtra(MmsDownloader.ExtraStub) is { } stubText ? Android.Net.Uri.Parse(stubText) : null;
        try
        {
            if (resultCode != Result.Ok)
            {
                MmsStubSweep.RecordAttempt(context, stub);
                PostDiagnostics.LogWarning("Mms", $"MMS download did not complete (result {resultCode}).");
                PostDiagnostics.LogText("Mms", $"MMS download did not complete (result {resultCode}); will retry");
                return;
            }

            var message = path is null || !File.Exists(path) ? null : MmsRetrieveConfParser.Parse(File.ReadAllBytes(path));
            if (message is null || !message.IsRetrieved)
            {
                MmsStubSweep.RecordAttempt(context, stub);
                PostDiagnostics.LogWarning("Mms", message is null
                    ? "A downloaded MMS could not be read."
                    : $"The carrier refused an MMS download (status {message.RetrieveStatus}).");
                PostDiagnostics.LogText("Mms", "MMS download could not be read; will retry");
                return;
            }

            var stored = MmsProviderWriter.StoreIncoming(context, message, DateTimeOffset.UtcNow);
            if (stub is not null)
            {
                context.ContentResolver?.Delete(stub, null, null);
                MmsStubSweep.Forget(context, stub);
            }
            MmsAcknowledgement.Send(context, intent.GetStringExtra(MmsDownloader.ExtraTransactionId)
                ?? message.TransactionId);

            if (stored is null)
            {
                PostDiagnostics.LogText("Mms", "MMS downloaded again; already stored");
                return;
            }

            PostDiagnostics.LogText("Mms", TextTrail.MmsDownloaded(
                (message.From is null ? 0 : 1) + message.To.Count + message.Cc.Count));
            var body = stored.Record.Text
                ?? AttachmentSummary.Describe(SmsLogReader.ReadMmsAttachments(context, stored.Id));
            SmsNotifications.NotifyIncoming(context, new IncomingSms(message.From, body));
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }
}

/// <summary>
/// Tells the carrier a downloaded MMS arrived (M-NotifyResp.ind, status Retrieved), as AOSP's
/// own messaging app does after every download. Without it some carriers announce the
/// message again. Best effort: a failed acknowledgement costs at most a duplicate notice,
/// which <see cref="MmsDownloader"/> and <see cref="MmsProviderWriter"/> both absorb.
/// </summary>
internal static class MmsAcknowledgement
{
    private const string Folder = "mms-outbox";
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);

    public static void Send(Context context, string? transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return;
        }
        try
        {
            var directory = new Java.IO.File(context.CacheDir, Folder);
            directory.Mkdirs();
            ClearOld(directory);

            var file = new Java.IO.File(directory, $"ack-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(file.AbsolutePath!, MmsComposer.ComposeNotifyResponse(transactionId));
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                context, $"{context.PackageName}.attachments", file);
            MmsAccess.GrantToPlatform(context, uri, ActivityFlags.GrantReadUriPermission);

            // No location: the platform posts it to the carrier's MMSC, which is AOSP's default
            // (it passes the content location only for a carrier configured to want it there).
            SmsManager.Default?.SendMultimediaMessage(context, uri, null, null, null);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Mms", "Could not acknowledge a downloaded MMS", e);
        }
    }

    // There is no callback for the acknowledgement, so its file is cleared on a later one.
    private static void ClearOld(Java.IO.File directory)
    {
        var cutoff = Java.Lang.JavaSystem.CurrentTimeMillis() - (long)KeepFor.TotalMilliseconds;
        foreach (var old in directory.ListFiles() ?? [])
        {
            if (old.Name.StartsWith("ack-", StringComparison.Ordinal) && old.LastModified() < cutoff)
            {
                old.Delete();
            }
        }
    }
}
