using Android.App;
using Android.OS;
using Android.Service.Notification;
using Android.Util;
using Codex.Domain.Calls;

namespace Flyleaf;

/// <summary>
/// Flyleaf's way into a phone call. The lockdown disables the status bar, and the device-owner
/// call that does so also disables notification alerts — so a call that rings while the phone is
/// in use never shows its heads-up card, and there is no shade to find it in afterwards. Calls were
/// ringing with nowhere to answer them. <see cref="TileNotificationListener"/> already sees every
/// notification; when one is a call, Flyleaf opens the call screen the dialer asked the platform to
/// show, and the home shelf carries a pinned way back while the call lasts.
/// </summary>
public static class CallScreen
{
    private const string Tag = "FlyleafCall";

    // Notification.EXTRA_CALL_TYPE, read by name so a pre-CallStyle notification reads as absent.
    private const string ExtraCallType = "android.callType";

    /// <summary>
    /// The call the home shelf offers a way back into, written by the listener (same process) on
    /// every notification change and read by <see cref="HomeActivity"/> on resume and on each
    /// change broadcast. A static rather than a broadcast extra because the home screen only hears
    /// broadcasts while it is in front, and a call can start while it is not.
    /// </summary>
    public static (CallNoticeKind Kind, PendingIntent? Target) Current { get; private set; }

    public static CallNoticeKind Classify(Notification? notification)
    {
        if (notification is null)
        {
            return CallNoticeKind.None;
        }
        return CallNotice.Classify(
            notification.Category,
            notification.Extras?.GetInt(ExtraCallType, CallNotice.CallTypeNone) ?? CallNotice.CallTypeNone,
            hasFullScreenIntent: notification.FullScreenIntent is not null,
            isOngoing: (notification.Flags & NotificationFlags.OngoingEvent) != 0);
    }

    /// <summary>
    /// What to open for a call: the full-screen intent for a ringing one — the very screen the
    /// platform would have shown had alerts been allowed — and the notification's own tap target
    /// for a call in progress, which is the dialer's in-call screen.
    /// </summary>
    public static PendingIntent? Target(Notification notification, CallNoticeKind kind) => kind == CallNoticeKind.Incoming
        ? notification.FullScreenIntent ?? notification.ContentIntent
        : notification.ContentIntent ?? notification.FullScreenIntent;

    public static void UpdateCurrent(IEnumerable<StatusBarNotification> active)
    {
        var calls = active
            .Where(sbn => sbn.Notification is not null)
            .Select(sbn => (Kind: Classify(sbn.Notification), sbn.Notification))
            .Where(call => call.Kind != CallNoticeKind.None)
            .ToList();
        var strongest = CallNotice.Strongest(calls.Select(call => call.Kind));
        var notification = calls.FirstOrDefault(call => call.Kind == strongest).Notification;
        Current = notification is null ? (CallNoticeKind.None, null) : (strongest, Target(notification, strongest));
    }

    /// <summary>
    /// Opts a PendingIntent send into starting an activity from the background with Flyleaf's own
    /// device-owner privilege (Android 14+; earlier versions need nothing). Shared with the locked
    /// hand-off, which starts the service's app from a broadcast receiver and learned the same lesson.
    /// </summary>
    internal static ActivityOptions OptInToBackgroundStart(ActivityOptions options)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            var mode = OperatingSystem.IsAndroidVersionAtLeast(36)
                ? BackgroundActivityStartMode.AllowAlways
                : BackgroundActivityStartMode.Allowed;
            options.SetPendingIntentBackgroundActivityStartMode(mode);
        }

        return options;
    }

    /// <summary>
    /// Opens the call screen. Flyleaf is usually not in front when a call rings (the owner is in
    /// some other app), and since Android 14 a PendingIntent sent from the background starts an
    /// activity only if the sender opts in with its own privilege — which Flyleaf has, as the
    /// device owner. Without the opt-in the send "succeeds" and nothing appears.
    /// </summary>
    public static bool Open(PendingIntent target)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                target.Send(OptInToBackgroundStart(ActivityOptions.MakeBasic()!).ToBundle());
            }
            else
            {
                target.Send();
            }
            return true;
        }
        catch (PendingIntent.CanceledException e)
        {
            // The dialer withdrew it between the post and the send — the call ended or was
            // answered elsewhere. Nothing to open.
            Log.Info(Tag, $"Call screen intent was cancelled before it could open: {e.Message}");
            return false;
        }
    }
}
