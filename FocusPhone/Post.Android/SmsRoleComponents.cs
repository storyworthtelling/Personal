using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

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
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.SmsDeliverReceiver",
    Exported = true,
    Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter([SmsRoleComponents.SmsDeliver], DataMimeType = "*/*")]
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
            SmsProviderWriter.PersistIncoming(context, intent);
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
/// Incoming MMS, which arrives as a WAP push rather than as a message. Post does not
/// assemble MMS parts yet, so this exists to satisfy the role requirement and to record
/// that something arrived rather than to drop it in silence.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.MmsDeliverReceiver",
    Exported = true,
    Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter([SmsRoleComponents.WapPushDeliver], DataMimeType = "application/vnd.wap.mms-message")]
public class MmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent) =>
        PostDiagnostics.LogWarning("Mms", "An MMS arrived; Post does not assemble MMS parts yet.");
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
