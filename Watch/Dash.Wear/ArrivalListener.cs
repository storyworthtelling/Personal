using Android.App;
using Android.Content;
using Android.Gms.Wearable;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Touch;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// Dash's ear for a text arriving (P14 B8; WD §7, A12, A35): Play services wakes it, with no process running, for
/// <c>dash/arrived</c> alone, which Post tells the wrist where the phone itself alerts for a text. The wrist gives its two
/// short taps once for each (<see cref="TextArrivals"/>): by day for everyone; overnight, whenever the watch's own Do Not
/// Disturb or Bedtime mode holds, only for a favourite, starred in the phone's contacts. Nothing is posted and nothing is
/// marked (WD §1); the tile is asked to be drawn again. Post's answers to Dash's asks never come here: they go to the
/// live ear while an ask waits (<see cref="PostLink"/>).
/// </summary>
[Service(Name = WatchIdentity.DashArrivalListenerName, Exported = true)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashArrivedPath)] // not copy: the Data Layer's scheme and any host
public sealed class ArrivalListener : WearableListenerService
{
    public override void OnMessageReceived(IMessageEvent messageEvent)
    {
        var path = messageEvent.Path;
        if (!string.Equals(path, WatchIdentity.DashArrivedPath, StringComparison.Ordinal))
        {
            return;
        }

        var read = DashCodec.Decode(path, messageEvent.GetData());
        if (read.Message is not DashArrived arrived)
        {
            DashLog.Warn(DashLogLines.Receipt(path, read, DashEnd.Dash));
            return;
        }
        DashLog.Info(DashLogLines.Receipt(path, read, DashEnd.Dash));

        var context = ApplicationContext ?? this;
        var buzz = TextArrivals.Shared.Heard(arrived, DateTimeOffset.UtcNow, Overnight.During(InterruptionsOf(context)));
        var buzzed = false;
        try
        {
            buzzed = buzz.Decision.Action == HapticAction.Buzz && buzz.Decision.Pattern is { } pattern && WatchBuzz.Play(context, pattern, buzz.Usage);
        }
        catch (Java.Lang.Exception failure)
        {
            // The platform refused the vibrator: the text is on People waiting all the same.
            DashLog.Quiet("DASH a text arrived but the wrist couldn’t buzz", failure);
        }
        DashLog.Info(DashLogLines.Outcome(path, buzzed ? DashOutcome.Buzzed : DashOutcome.Quiet));
        PeopleTileService.Refresh(context);
    }

    // The watch's own Do Not Disturb, as the platform says it now; reading it needs no permission.
    private static Interruptions InterruptionsOf(Context context) =>
        context.GetSystemService(Context.NotificationService) is NotificationManager manager
            ? manager.CurrentInterruptionFilter switch
            {
                InterruptionFilter.All => Interruptions.All,
                InterruptionFilter.Priority => Interruptions.Priority,
                InterruptionFilter.Alarms => Interruptions.Alarms,
                InterruptionFilter.None => Interruptions.None,
                _ => Interruptions.Unknown,
            }
            : Interruptions.Unknown;
}
