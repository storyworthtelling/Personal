using Pulp.Domain.Diagnostics;
using Codex.Ui;
using Android.App;
using Android.Content;
using Android.OS;
using Pulp.Domain.LockedOpen;

// Flyleaf declares RECEIVE_LOCKED_OPEN at signature level; Pulp must hold it for the broadcast to land.
[assembly: UsesPermission(LockedOpenHandOff.Permission)]

namespace Pulp.LockedOpen;

/// <summary>
/// Asks Flyleaf to open a title locked (D288). An explicit broadcast naming Flyleaf's receiver under
/// the signature permission, so nothing else can hear it and nothing else can send one. It builds no
/// view intent and knows nothing of children: <c>HandOffExecutor</c> calls it only after the planner
/// has allowed the hand-off, with the planner's own package and link (the one-door guards hold that).
/// </summary>
internal static class LockedOpenCourier
{
    /// <summary>Whether Flyleaf is on this phone and visible to Pulp (the manifest declares the query).</summary>
    public static bool FlyleafPresent(Context context)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(LockedOpenHandOff.Package, (Android.Content.PM.PackageInfoFlags)0);
            return true;
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks Flyleaf with an <i>ordered</i> broadcast and reads its result code as the answer (D301): a plain
    /// broadcast cannot say whether anyone acted on it, so a refusal (not device owner, the hidden sets
    /// differ, the app is blocked or suspended) was invisible and the plain hand-off never ran.
    /// <paramref name="fallBack"/> runs on the main thread, once, when Flyleaf answers anything but
    /// "started" or does not answer within <see cref="LockedOpenHandOff.AnswerTimeout"/>.
    /// </summary>
    /// <returns>True when the ask was handed to the system; false when none could be sent, so the caller
    /// falls back to the plain hand-off itself.</returns>
    public static bool Send(Context context, string package, Uri link, Action fallBack)
    {
        ArgumentNullException.ThrowIfNull(fallBack);
        var asking = TapWatch.Action(PulpKeyActions.LockedOpen, LockedOpenHandOff.AnswerTimeout + TimeSpan.FromSeconds(5));
        try
        {
            var intent = new Intent(LockedOpenHandOff.Action);
            intent.SetComponent(new ComponentName(LockedOpenHandOff.Package, LockedOpenHandOff.Receiver));
            foreach (var (key, value) in LockedOpenHandOff.Extras(package, link))
            {
                intent.PutExtra(key, value);
            }

            var main = new Handler(Looper.MainLooper!);
            var answer = new LockedOpenAnswer();
            void Resolve(int? code)
            {
                if (answer.ShouldFallBack(code))
                {
                    HeadLog.Info("Pulp", PulpLogLines.LockedOpen(code));
                    asking.Failed(PulpLogLines.LockedOpen(code));
                    fallBack();
                }
                else if (code == LockedOpenHandOff.ResultStarted)
                {
                    HeadLog.Info("Pulp", PulpLogLines.LockedOpen(code));
                    asking.Succeeded();
                }
            }

            main.PostDelayed(() => Resolve(null), (long)LockedOpenHandOff.AnswerTimeout.TotalMilliseconds);
            context.SendOrderedBroadcast(
                intent,
                LockedOpenHandOff.Permission,
                new AnswerReceiver(code => Resolve(code)),
                main,
                LockedOpenHandOff.ResultRefused,
                null,
                null);
            return true;
        }
        catch (Exception e)
        {
            HeadLog.Warn("Pulp", PulpLogLines.LockedOpenNotSent(e.GetType().Name), e);
            asking.Failed(PulpLogLines.LockedOpenNotSent(e.GetType().Name));
            return false;
        }
    }

    // The ordered broadcast's final receiver: it runs once Flyleaf's receiver (or nobody) has finished.
    private sealed class AnswerReceiver(Action<int> answered) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => answered(ResultCode == Result.Ok
            ? LockedOpenHandOff.ResultStarted
            : LockedOpenHandOff.ResultRefused);
    }
}
