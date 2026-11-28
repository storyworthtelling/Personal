using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Android.Content;
using Android.OS;
using Pulp.Domain.LockedOpen;

namespace Pulp.LockedOpen;

/// <summary>
/// Asks Flyleaf to lift the Entertainment block on a streaming service for the length of the title Pulp is
/// about to open (the timed release). An explicit ordered broadcast naming Flyleaf's receiver under the
/// signature permission, so nothing else hears it and nothing else can send one.
///
/// The continuation runs exactly once, on the main thread: when Flyleaf answers, or after
/// <see cref="TimedReleaseHandOff.AnswerTimeout"/> if it does not. Either way the title is then started, so a
/// Flyleaf that is slow or absent never keeps a title shut; the release only ever makes a start possible.
/// </summary>
internal static class TimedReleaseCourier
{
    private const string Contract = "timed-release";

    /// <summary>Whether a release can be asked at all (Flyleaf is here and Pulp may reach it).</summary>
    /// <returns>True when the ask was handed to the system and <paramref name="continueWith"/> will run; false when
    /// none could be sent, so the caller starts the title itself at once.</returns>
    public static bool Send(Context context, string package, Uri link, int minutes, Action continueWith)
    {
        ArgumentNullException.ThrowIfNull(continueWith);
        var before = ContractCourier.Before(context, TimedReleaseHandOff.Package, LockedOpenHandOff.Permission);
        if (before != ContractSendOutcome.Sent)
        {
            HeadLog.Info(LogSources.Contract(Contract), ContractBroadcast.SendLine(Contract, before));
            return false;
        }

        try
        {
            var intent = new Intent(TimedReleaseHandOff.Action);
            intent.SetComponent(new ComponentName(TimedReleaseHandOff.Package, TimedReleaseHandOff.Receiver));
            intent.PutExtra(ContractBroadcast.VersionExtra, ContractBroadcast.Version);
            intent.PutExtra(TimedReleaseHandOff.PackageExtra, package);
            intent.PutExtra(TimedReleaseHandOff.LinkExtra, link.AbsoluteUri);
            intent.PutExtra(TimedReleaseHandOff.MinutesExtra, minutes);

            var main = new Handler(Looper.MainLooper!);
            var settled = 0;
            void Resolve(int? code)
            {
                if (Interlocked.Exchange(ref settled, 1) != 0)
                {
                    return;
                }

                HeadLog.Info("Pulp", "Timed release " + (code == TimedReleaseHandOff.ResultReleased ? "granted" : "not granted"));
                continueWith();
            }

            main.PostDelayed(() => Resolve(null), (long)TimedReleaseHandOff.AnswerTimeout.TotalMilliseconds);
            context.SendOrderedBroadcast(
                intent,
                LockedOpenHandOff.Permission,
                new AnswerReceiver(code => Resolve(code)),
                main,
                (Result)TimedReleaseHandOff.ResultNoAnswer,
                null,
                null);
            HeadLog.Info(LogSources.Contract(Contract), ContractBroadcast.SendLine(Contract, ContractSendOutcome.Sent));
            return true;
        }
        catch (Exception e)
        {
            HeadLog.Info(LogSources.Contract(Contract), ContractBroadcast.SendLine(Contract, ContractSendOutcome.Failed, e.GetType().Name));
            HeadLog.Warn("Pulp", "The timed release could not be sent: " + e.GetType().Name, e);
            return false;
        }
    }

    // The ordered broadcast's final receiver: it runs once Flyleaf's receiver (or nobody) has finished.
    // Flyleaf sets Ok or Canceled; anything else is the initial code left standing, so nobody answered.
    private sealed class AnswerReceiver(Action<int> answered) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => answered(ResultCode switch
        {
            Result.Ok => TimedReleaseHandOff.ResultReleased,
            Result.Canceled => TimedReleaseHandOff.ResultRefused,
            _ => TimedReleaseHandOff.ResultNoAnswer,
        });
    }
}
