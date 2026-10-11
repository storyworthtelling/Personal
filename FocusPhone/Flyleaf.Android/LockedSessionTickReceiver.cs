using Android.App;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// The clock behind a locked hand-off: a long pause and the six-hour cap are about time passing, and
/// nothing else wakes Flyleaf when nothing is happening. Not exported; only the alarm Flyleaf set
/// itself reaches it.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class LockedSessionTickReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            LockedSessionController.OnTick(context);
        }
    }
}
