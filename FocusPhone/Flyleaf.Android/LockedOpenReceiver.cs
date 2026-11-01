using Codex.Ui;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Codex.Domain.Apps;

// Pulp's locked hand-off contract (LockedOpenHandoff). Pulp holds this permission to send; declaring
// it here at signature level is what makes the receiver's Permission enforceable at all, since a
// permission nobody declares is never granted to anyone.
[assembly: Permission(Name = LockedOpenHandoff.Permission, ProtectionLevel = Protection.Signature)]

namespace Flyleaf;

/// <summary>
/// Receives Pulp's ask to open a title locked (D288). It only reads the two extras; everything that
/// decides whether and how is <see cref="LockedOpenGate"/> and <c>LockedSessionPlan</c> in Domain,
/// carried out by <see cref="LockedSessionController"/>. An unreadable payload is ignored.
///
/// Pulp sends this as an <i>ordered</i> broadcast and reads the result code as the answer (D301): started
/// only when the controller says a session actually began, refused for everything else, so Pulp can fall
/// back to the plain hand-off instead of trusting a send that proves nothing. The controller runs
/// synchronously inside <c>OnReceive</c>, so the code is set before the broadcast finishes and no
/// <c>GoAsync</c> pending result is needed.
/// </summary>
[BroadcastReceiver(
    Name = LockedOpenHandoff.Receiver,
    Exported = true,
    Enabled = true,
    Permission = LockedOpenHandoff.Permission)]
[IntentFilter([LockedOpenHandoff.Action])]
public class LockedOpenReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != LockedOpenHandoff.Action)
        {
            return;
        }

        var package = intent.GetStringExtra(LockedOpenHandoff.PackageExtra);
        var link = intent.GetStringExtra(LockedOpenHandoff.LinkExtra);
        if (!LockedOpenCodec.TryRead(package, link, out var request))
        {
            Android.Util.Log.Warn("Flyleaf", "A locked-open request carried no readable package and link; it was ignored.");
            Answer(LockedOpenHandoff.ResultRefused);
            return;
        }

        var started = LockedSessionController.Start(context, request);
        Answer(started ? LockedOpenHandoff.ResultStarted : LockedOpenHandoff.ResultRefused);
    }

    // A plain (unordered) sender has no result to set; an ordered one reads this.
    private void Answer(int code)
    {
        if (IsOrderedBroadcast)
        {
            ResultCode = code == LockedOpenHandoff.ResultStarted ? Result.Ok : Result.Canceled;
        }
    }
}
