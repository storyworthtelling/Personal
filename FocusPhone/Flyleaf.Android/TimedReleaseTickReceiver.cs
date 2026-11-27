using Android.App;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// The alarm that closes a timed release's window. Not exported: only the alarm Flyleaf set itself reaches it.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class TimedReleaseTickReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            TimedReleaseController.OnExpiry(context);
        }
    }
}
