using Android.App;
using Android.Content;
using Android.Telephony;

namespace Post;

/// <summary>
/// Watches for a call ending, so Post can offer to write the same note
/// <see cref="MailboxArea.WriteCallNote"/> already writes from a long-press — automatically,
/// instead of relying on the owner to remember it right after hanging up.
///
/// <see cref="TelephonyManager.ActionPhoneStateChanged"/> (the older broadcast) rather than
/// the API 31+ <c>TelephonyCallback</c>: that callback has to be registered by a running
/// component via <c>RegisterTelephonyCallback</c> and is gone the moment the process dies,
/// which is exactly the kind of window a phone call sits in. A manifest-declared receiver
/// survives that — ACTION_PHONE_STATE_CHANGED is one of the implicit-broadcast exceptions
/// Android still delivers to a manifest receiver with no process running, the same
/// exception SMS_DELIVER relies on for <see cref="SmsDeliverReceiver"/> — and it is a
/// protected system broadcast (only the platform may send it), so this needs no Permission=
/// guard the way that receiver needs BROADCAST_SMS. What it does need is READ_PHONE_STATE
/// (see ManifestPermissions.cs): without it Android never delivers the broadcast at all.
///
/// A call counts as "ended" when the state goes OFFHOOK (connected — an answered incoming
/// call or a placed outgoing one) and THEN Idle. A missed, declined, or never-answered call
/// never reaches OFFHOOK, so it never reaches here either — there is nothing to write a
/// note about a call nobody was actually on. The OFFHOOK sighting is kept in
/// SharedPreferences rather than a field, since this receiver is a fresh instance per
/// broadcast and the process itself can die between the two halves of a long call.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.CallStateReceiver",
    Exported = true)]
[IntentFilter([CallStateReceiver.ActionPhoneStateChanged])]
public class CallStateReceiver : BroadcastReceiver
{
    public const string ActionPhoneStateChanged = "android.intent.action.PHONE_STATE";

    private const string PreferencesName = "callState";
    private const string WasOffhookKey = "wasOffhook";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != ActionPhoneStateChanged)
        {
            return;
        }

        var prefs = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
        var state = intent.GetStringExtra(TelephonyManager.ExtraState);

        if (state == TelephonyManager.ExtraStateOffhook)
        {
            prefs.Edit()!.PutBoolean(WasOffhookKey, true)!.Apply();
            return;
        }

        if (state != TelephonyManager.ExtraStateIdle || !prefs.GetBoolean(WasOffhookKey, false))
        {
            return; // RINGING, or an IDLE with no OFFHOOK before it — not a call ending.
        }
        prefs.Edit()!.PutBoolean(WasOffhookKey, false)!.Apply();

        // The call-log row for the call that just ended may not have landed yet, and
        // reading it (with a short wait-and-retry) is more work than a broadcast receiver
        // should do on its own thread — GoAsync keeps this instance alive long enough to
        // finish off the main thread without Android tearing the process down first.
        var appContext = context.ApplicationContext!;
        var pending = GoAsync()!;
        Task.Run(() =>
        {
            try
            {
                CallEndedNotePrompt.Handle(appContext);
            }
            catch (Exception e)
            {
                // Never throw out of a broadcast receiver: it kills the process, and this
                // one shares it with SmsDeliverReceiver — a missed note prompt is a far
                // smaller loss than incoming texts failing to persist.
                Android.Util.Log.Warn("Post", $"Call-ended handling failed: {e}");
                PostDiagnostics.LogWarning("CallNote", "Call-ended handling failed", e);
            }
            finally
            {
                pending.Finish();
            }
        });
    }
}
