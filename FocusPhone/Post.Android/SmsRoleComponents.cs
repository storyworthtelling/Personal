using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The four manifest components Android requires before it will even <i>offer</i> Post the
/// SMS role. This is not a runtime check that fails loudly: an app missing any one of
/// them is silently absent from the "default SMS app" list, with nothing anywhere saying
/// why — which is why <c>FrozenInvariantsTests</c> asserts all four exist.
///
/// They live in one file because they are one requirement, and because scattering four
/// near-empty components across four files hides how much of this is a checklist rather
/// than a design.
/// </summary>
internal static class SmsRoleComponents
{
    /// <summary>The intent action Android hands the default SMS app for each incoming text.</summary>
    public const string SmsDeliver = "android.provider.Telephony.SMS_DELIVER";

    /// <summary>The MMS equivalent, delivered as a WAP push.</summary>
    public const string WapPushDeliver = "android.provider.Telephony.WAP_PUSH_DELIVER";

    /// <summary>The quick-reply action a call screen offers ("can't talk, texting you").</summary>
    public const string RespondViaMessage = "android.intent.action.RESPOND_VIA_MESSAGE";
}

/// <summary>
/// Incoming texts, delivered here because Post holds the role. As role holder Post is the
/// only thing that can write to the system SMS provider, and it is <b>responsible</b> for
/// doing so: Android stops persisting incoming messages itself the moment a third-party
/// app takes over. Skip this and the phone's own record of the owner's texts simply stops
/// — silently, and unrecoverably for anything received while it was skipped.
///
/// The provider is the durable record; Post's own timeline cache is rebuilt from it.
///
/// Notifying the owner is Post's job too, for the same reason: Google Messages used to
/// raise the notification (and the buzz) for every incoming text, and it stops seeing SMS
/// broadcasts the moment Post takes the role. <see cref="SmsNotifications"/> is what
/// replaces it.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.SmsDeliverReceiver",
    Exported = true,
    Permission = "android.permission.BROADCAST_SMS")]
// No data type, deliberately. Android resolves a BARE Intent(SMS_DELIVER_ACTION)
// when deciding who may be the default SMS app, and an untyped intent cannot match a
// typed filter — a mimeType here disqualifies Post from the role, silently, and stops
// real incoming texts reaching this receiver. Guarded by FrozenInvariantsTests.
[IntentFilter([SmsRoleComponents.SmsDeliver])]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != SmsRoleComponents.SmsDeliver)
        {
            return;
        }

        try
        {
            var incoming = SmsProviderWriter.PersistIncoming(context, intent);
            if (incoming is not null)
            {
                PostDiagnostics.LogText("Sms", TextTrail.Sms(
                    incoming.Address, incoming.SentAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, incoming.Parts));
                SmsNotifications.NotifyIncoming(context, incoming);
            }
        }
        catch (Exception e)
        {
            // Never throw out of a broadcast receiver: it kills the process and Android
            // does not redeliver, so the text would be lost twice over.
            Android.Util.Log.Warn("Post", $"Could not persist an incoming text: {e}");
            PostDiagnostics.LogWarning("Sms", "Could not persist an incoming text", e);
        }
    }
}

/// <summary>
/// Incoming MMS, which arrives as a WAP push carrying only a notification — a transaction id
/// and a URL to fetch the actual message from, not the message itself. As role holder Post
/// is responsible for asking the platform to fetch it (<see cref="MmsDownloader"/>); the
/// notification PDU is parsed by the pure, unit-tested <see cref="MmsNotificationParser"/>.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.MmsDeliverReceiver",
    Exported = true,
    Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter([SmsRoleComponents.WapPushDeliver], DataMimeType = "application/vnd.wap.mms-message")]
public class MmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            var pdu = intent?.GetByteArrayExtra("data");
            var notification = MmsNotificationParser.Parse(pdu);
            if (notification is null)
            {
                // The layout, never the content, so the next export can say why.
                var shape = pdu is null ? "no data" : MmsPduShape.Describe(pdu);
                if (pdu is not null && MmsPduShape.MessageType(pdu) is { } type and not 0x82)
                {
                    // A delivery or read report on something the owner sent — not a message.
                    PostDiagnostics.LogText("Mms", $"MMS report from the carrier, not a message ({shape})");
                }
                else
                {
                    PostDiagnostics.LogWarning("Mms", $"An MMS notification arrived but could not be read ({shape}). "
                        + $"Layout: {MmsPduShape.Redacted(pdu ?? [])}");
                    PostDiagnostics.LogText("Mms", $"MMS notice unreadable ({shape})");
                }
                return;
            }

            if (pdu is { Length: > 0 } && pdu[0] != 0x8C)
            {
                // Confirms what wraps the notice on this phone, by shape alone.
                PostDiagnostics.LogText("Mms", $"MMS notice read past a wrapper: {MmsPduShape.Redacted(pdu, limit: 24)}");
            }
            PostDiagnostics.LogText("Mms", TextTrail.MmsAnnounced());
            MmsDownloader.RequestDownload(context, notification);
        }
        catch (Exception e)
        {
            // Never throw out of a broadcast receiver: it kills the process, and the
            // carrier still expects a WAP-push response either way.
            Android.Util.Log.Warn("Post", $"Could not start an MMS download: {e}");
            PostDiagnostics.LogWarning("Mms", "Could not start an MMS download", e);
        }
    }
}

/// <summary>
/// The system's "reply by message" service, offered from a call screen. Required for the
/// role; Post has nothing to add to a canned reply, so it accepts the bind and does
/// nothing rather than pretending to send.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.communication.RespondViaMessageService",
    Exported = true,
    Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
[IntentFilter(
    [SmsRoleComponents.RespondViaMessage],
    Categories = [Intent.CategoryDefault],
    DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class RespondViaMessageService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;
}
