using Android.App;
using Android.Content;
using Android.Telecom;
using Watch.Domain;
using Watch.Domain.Dash;

namespace Dash.Wear;

/// <summary>
/// Unknown callers on the wrist (P14 B8, W-5.1.9; A12): "unknown numbers ring only if they call twice in a few minutes".
/// The watch's own Phone app rings and carries every call (A59), so the decision is the watch's: the platform binds the
/// app holding the call-screening role (asked for once, from People waiting) for each incoming call from a number not in
/// the watch's contacts, before it rings. A first call from such a number shows without ringing, and can still be
/// answered and is still in the call log and the missed-call notice; the same caller again within
/// <see cref="RepeatCallers.Window"/> rings (<see cref="RepeatCallers"/>), day and night alike. Calls from contacts never
/// come here, so overnight the favourites-and-repeat-callers rule for them is the watch's own Do Not Disturb, set on the
/// watch (a setup step: an app may not set it without an access Wear OS offers no screen for).
///
/// <para>The one number the wrist ever holds is the one the platform hands this service, and it is turned at once into a
/// digest sealed by a key the platform's keystore keeps (<see cref="CallerKey"/>, <see cref="CallerSeal"/>): the number is
/// never written down, logged, shown or sent. What is written down is the last few callers' sealed digests and moments,
/// for the window and no longer, since the process can die between two calls minutes apart.</para>
/// </summary>
[Service(Name = WatchIdentity.DashCallScreenName, Exported = true, Permission = "android.permission.BIND_SCREENING_SERVICE")] // not copy: the platform's permission
[IntentFilter([CallScreeningService.ServiceInterface])]
public sealed class CallScreen : CallScreeningService
{
    private static readonly object Gate = new();

    public override void OnScreenCall(Call.Details details)
    {
        var response = new CallResponse.Builder();
        if (details.CallDirection == CallDirection.Incoming && Heard(details) == RingVerdict.Silence)
        {
            response.SetSilenceCall(true);
            DashLog.Info("DASH an unknown number rang; it shows without ringing");
        }
        // The platform waits five seconds for an answer; this one is given at once.
        RespondToCall(details, response.Build()!);
    }

    // The callers kept across a restart, this one added, and kept again for the window. A keystore or a store that won't
    // answer is logged by its shape, and the call rings: a ring the owner didn't want is better than a call never heard.
    private RingVerdict Heard(Call.Details details)
    {
        try
        {
            lock (Gate)
            {
                var kept = GetSharedPreferences(WatchIdentity.DashCallersPreferences, FileCreationMode.Private)!;
                var now = DateTimeOffset.UtcNow;
                var callers = RepeatCallers.From(kept.GetString(WatchIdentity.DashCallersKey, null), now);
                var verdict = callers.Heard(CallerKey.Of(details.GetHandle()?.SchemeSpecificPart, CallerSeal.Digest), now);
                kept.Edit()!.PutString(WatchIdentity.DashCallersKey, callers.Record(now))!.Apply();
                if (verdict == RingVerdict.Ring)
                {
                    DashLog.Info("DASH an unknown number rang again within minutes; it rings");
                }
                return verdict;
            }
        }
        catch (Java.Lang.Exception failure)
        {
            DashLog.Quiet("DASH an unknown number rang and couldn’t be judged; it rings", failure);
            return RingVerdict.Ring;
        }
    }
}
