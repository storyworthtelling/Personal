using Android.App;
using Android.Content;
using Android.Telephony;
using Watch.Domain;
using Watch.Domain.Dash;
using CallState = Watch.Domain.Dash.CallState;

namespace Dash.Wear;

/// <summary>
/// Dash's ear for the watch's own call state (P14 B7, W-5.1.5; A13). The watch's Phone app rings and carries the call
/// (A59); the platform tells any app holding READ_PHONE_STATE when the call state changes, by a broadcast it still
/// delivers to a manifest receiver with no process running (<see cref="TelephonyManager.ActionPhoneStateChanged"/> is one
/// of the implicit-broadcast exceptions, and a protected one only the platform sends). Post on the phone hears its own
/// calls the same way.
///
/// <para>It reads the state alone, never the number the extras can carry for an app that also reads the call log, and
/// hands it to <see cref="AfterCall"/>, which keeps the end in memory. It does no work of its own: Android does not let
/// an app open a screen from the background, so the note is offered when Dash is next opened (People waiting asks Post
/// which call it was then). Nothing buzzes and nothing is posted.</para>
/// </summary>
[BroadcastReceiver(Name = WatchIdentity.DashCallReceiverName, Exported = true)]
[IntentFilter([TelephonyManager.ActionPhoneStateChanged])]
public sealed class CallEndReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != TelephonyManager.ActionPhoneStateChanged || StateOf(intent.GetStringExtra(TelephonyManager.ExtraState)) is not { } state)
        {
            return;
        }
        if (AfterCall.Shared.Heard(state, DateTimeOffset.UtcNow) is not null)
        {
            DashLog.Info("DASH a call ended; the note waits for Dash to open");
        }
    }

    // The platform's three words for the call state; anything else is not one.
    private static CallState? StateOf(string? said)
    {
        if (string.Equals(said, TelephonyManager.ExtraStateIdle, StringComparison.Ordinal))
        {
            return CallState.Idle;
        }
        if (string.Equals(said, TelephonyManager.ExtraStateRinging, StringComparison.Ordinal))
        {
            return CallState.Ringing;
        }
        return string.Equals(said, TelephonyManager.ExtraStateOffhook, StringComparison.Ordinal) ? CallState.OffHook : null;
    }
}
