using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.OS;
using Codex.Domain.Apps;

namespace Flyleaf;

/// <summary>
/// Lifts the Entertainment block on one streaming service for the length of the title Pulp is playing, and
/// puts it back when that length is up (the Entertainment block). Every decision is in Domain:
/// <see cref="TimedReleaseGate"/> for what may be asked, <see cref="TimedReleases"/> for which windows are
/// open. This class reads the device, applies the change, and keeps the alarm that closes each window.
///
/// The window closes by a policy pass: the release is recorded, so <c>PolicyManager</c> plans the service
/// as not suspended while it is open, and suspends it again once it has closed. A closed window is never
/// trusted to a lost alarm: every read of the windows ignores the ones past their end.
/// </summary>
internal static class TimedReleaseController
{
    private const string Tag = "FlyleafTimed";
    private const int TickRequestCode = 2883;

    private static readonly object Gate = new();

    /// <summary>True when the service was released for the window; false when it was refused or could not be.</summary>
    public static bool Release(Context context, LockedOpenRequest request, int minutes)
    {
        lock (Gate)
        {
            if (!PolicyManager.IsDeviceOwner(context))
            {
                HeadLog.Warn(Tag, "Not device owner; a timed release was ignored. This is expected during development.");
                return false;
            }

            var refusal = TimedReleaseGate.Decide(
                request,
                new HiddenAppsStore(context).Load(),
                new BlocklistStore(context).Load().BlockedPackages.ToHashSet(StringComparer.Ordinal));
            if (refusal != LockedOpenRefusal.None)
            {
                HeadLog.Warn(Tag, $"A timed release for {request.Package} was refused: {refusal}.");
                return false;
            }

            var store = new TimedReleaseStore(context);
            var previous = store.Load();
            var now = DateTimeOffset.UtcNow;
            var until = TimedReleases.Until(now, minutes);
            // The window is recorded first: whatever happens next, a later start knows what to close.
            store.Save(TimedReleases.With(TimedReleases.Pruned(previous, now), request.Package, until));
            try
            {
                var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
                var admin = PolicyManager.AdminComponent(context);
                var failed = dpm.SetPackagesSuspended(admin, [request.Package], false);
                if (failed is { Length: > 0 })
                {
                    throw new InvalidOperationException($"{request.Package} could not be un-suspended for the title.");
                }

                ArmTick(context, TimedReleases.NextExpiry(TimedReleases.With(previous, request.Package, until), now));
                HeadLog.Info(Tag, $"A timed release was granted for {minutes} minutes.");
                return true;
            }
            catch (Exception exception)
            {
                // The windows before this ask are put back as they were; nothing stays open on a failed release.
                HeadLog.Warn(Tag, $"The timed release failed and is being undone: {exception.Message}");
                store.Save(TimedReleases.Pruned(previous, now));
                return false;
            }
        }
    }

    /// <summary>The alarm for a window's end: suspend what is no longer released, and arm the next end.</summary>
    public static void OnExpiry(Context context)
    {
        lock (Gate)
        {
            var store = new TimedReleaseStore(context);
            var stored = store.Load();
            var now = DateTimeOffset.UtcNow;
            var open = TimedReleases.Pruned(stored, now);
            if (open.Count != stored.Count)
            {
                store.Save(open);
                // The policy pass suspends whatever is no longer released (the service is planned suspended again).
                if (PolicyManager.IsDeviceOwner(context))
                {
                    PolicyManager.ApplyCurrentPolicy(context);
                }
            }

            ArmTick(context, TimedReleases.NextExpiry(open, now));
        }
    }

    /// <summary>Process start: a window a reboot cut short has lost its alarm, so it is armed again here.</summary>
    public static void OnProcessStart(Context context)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            var open = TimedReleases.Pruned(new TimedReleaseStore(context).Load(), now);
            ArmTick(context, TimedReleases.NextExpiry(open, now));
        }
    }

    private static PendingIntent TickIntent(Context context) => PendingIntent.GetBroadcast(
        context,
        TickRequestCode,
        new Intent(context, typeof(TimedReleaseTickReceiver)),
        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    // Nothing open means nothing to wake for: the alarm is cancelled rather than left to fire into nothing.
    private static void ArmTick(Context context, DateTimeOffset? at)
    {
        var alarms = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarms is null)
        {
            return;
        }

        if (at is not { } end)
        {
            alarms.Cancel(TickIntent(context));
            return;
        }

        var delay = end - DateTimeOffset.UtcNow;
        alarms.SetAndAllowWhileIdle(
            AlarmType.ElapsedRealtime,
            SystemClock.ElapsedRealtime() + (long)Math.Max(1000, delay.TotalMilliseconds),
            TickIntent(context));
    }
}
