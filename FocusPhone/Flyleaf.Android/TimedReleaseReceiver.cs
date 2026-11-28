using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Android.App;
using Android.Content;
using Codex.Domain.Apps;

namespace Flyleaf;

/// <summary>
/// Receives Pulp's ask to lift the Entertainment block on a streaming service for a title's length. It only
/// reads the extras; what may be asked is <see cref="TimedReleaseGate"/> and the window is kept by
/// <see cref="TimedReleaseController"/>. An unreadable payload, or a length outside the honoured range, is
/// refused: Pulp then starts the title as it always did.
///
/// An <i>ordered</i> broadcast, read the same way as the locked open (D301): Pulp starts the title only after
/// this answer, so the start cannot race the un-suspend. The controller runs synchronously inside
/// <c>OnReceive</c>, so the code is set before the broadcast finishes.
/// </summary>
[BroadcastReceiver(
    Name = TimedReleaseHandoff.Receiver,
    Exported = true,
    Enabled = true,
    Permission = LockedOpenHandoff.Permission)]
[IntentFilter([TimedReleaseHandoff.Action])]
public class TimedReleaseReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != TimedReleaseHandoff.Action)
        {
            return;
        }

        var version = ContractBroadcast.ReadVersion(intent.GetIntExtra(ContractBroadcast.VersionExtra, 0));
        HeadLog.Info(LogSources.Contract("timed-release"), ContractBroadcast.ReceiptLine("timed-release", version));
        if (!ContractBroadcast.Accepts(version))
        {
            Answer(TimedReleaseHandoff.ResultRefused);
            return;
        }

        var package = intent.GetStringExtra(TimedReleaseHandoff.PackageExtra);
        var link = intent.GetStringExtra(TimedReleaseHandoff.LinkExtra);
        var minutes = intent.GetStringExtra(TimedReleaseHandoff.MinutesExtra);
        if (!LockedOpenCodec.TryRead(package, link, out var request) || !TimedReleaseGate.TryReadMinutes(minutes, out var length))
        {
            Android.Util.Log.Warn("Flyleaf", "A timed release carried no readable package, link or length; it was ignored.");
            Answer(TimedReleaseHandoff.ResultRefused);
            return;
        }

        Answer(TimedReleaseController.Release(context, request, length)
            ? TimedReleaseHandoff.ResultReleased
            : TimedReleaseHandoff.ResultRefused);
    }

    // A plain (unordered) sender has no result to set; an ordered one reads this.
    private void Answer(int code)
    {
        if (IsOrderedBroadcast)
        {
            ResultCode = code == TimedReleaseHandoff.ResultReleased ? Result.Ok : Result.Canceled;
        }
    }
}
