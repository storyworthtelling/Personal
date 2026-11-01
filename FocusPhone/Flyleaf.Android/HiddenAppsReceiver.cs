using Codex.Ui;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Codex.Domain.Apps;

// Pulp's hidden-apps contract (HiddenAppsHandoff). Pulp holds this permission to send; declaring it
// here at signature level is what makes the receiver's Permission enforceable at all, since a
// permission nobody declares is never granted to anyone.
[assembly: Permission(Name = HiddenAppsHandoff.Permission, ProtectionLevel = Protection.Signature)]

namespace Flyleaf;

/// <summary>
/// Receives the set of streaming apps Pulp wants out of the tray (D8, D92) and persists it. Only a
/// well-formed list replaces the stored set; a missing or unreadable payload is ignored, so a bad
/// send never un-hides anything. The next shelf build (OnResume) reads the store.
/// </summary>
[BroadcastReceiver(
    Name = HiddenAppsHandoff.Receiver,
    Exported = true,
    Enabled = true,
    Permission = HiddenAppsHandoff.Permission)]
[IntentFilter([HiddenAppsHandoff.Action])]
public class HiddenAppsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != HiddenAppsHandoff.Action)
        {
            return;
        }

        var text = intent.GetStringExtra(HiddenAppsHandoff.PackagesExtra);
        if (!HiddenAppsCodec.TryDecode(text, out var hidden))
        {
            Android.Util.Log.Warn("Flyleaf", "Hidden-apps broadcast carried no readable list; the stored set is unchanged.");
            return;
        }

        new HiddenAppsStore(context).Save(hidden);
    }
}
