using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Telecom;
using Android.Util;
using Codex.Domain.Apps;
using Codex.Domain.Policy;

namespace Flyleaf;

/// <summary>
/// Carries out a locked hand-off (D288, D297): the thin Android half. Every decision is made by the
/// unit-tested <see cref="LockedOpenGate"/>, <see cref="LockedSessionPlan"/> and
/// <see cref="LockedSessionRules"/> in Domain; this class reads the device's facts, applies the plan
/// verbatim, and persists the <see cref="LockedSessionRecord"/> first and clears it last so that a
/// death in between is undone on the next start.
///
/// It is the only place that touches the lock task allowlist or features, and the only place that
/// un-suspends anything for a request — and then only the one service the request named, only when
/// it is suspended, and never one the plan hard-suspends. <c>PolicyManager</c> does neither.
/// </summary>
internal static class LockedSessionController
{
    private const string Tag = "FlyleafLocked";
    private const int TickRequestCode = 2881;

    private static readonly object Gate = new();

    /// <summary>True when a locked session was started; false when it was refused or could not be.</summary>
    public static bool Start(Context context, LockedOpenRequest request)
    {
        lock (Gate)
        {
            if (!PolicyManager.IsDeviceOwner(context))
            {
                Log.Warn(Tag, "Not device owner; a locked open was ignored. This is expected during development.");
                return false;
            }

            var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
            var admin = PolicyManager.AdminComponent(context);
            var policySuspended = PolicyManager.PlannedSuspendedPackages(context);
            var blocked = new BlocklistStore(context).Load().BlockedPackages.ToHashSet(StringComparer.Ordinal);

            var refusal = LockedOpenGate.Decide(request, new HiddenAppsStore(context).Load(), blocked, policySuspended);
            if (refusal != LockedOpenRefusal.None)
            {
                Log.Warn(Tag, $"A locked open for {request.Package} was refused: {refusal}.");
                return false;
            }

            var store = new LockedSessionStore(context);
            var facts = new LockedStartFacts(
                request.Package,
                dpm.GetLockTaskPackages(admin) ?? [],
                (int)dpm.GetLockTaskFeatures(admin),
                IsSuspended(dpm, admin, request.Package),
                policySuspended,
                DialerPackages(context),
                context.PackageName!,
                DateTimeOffset.UtcNow,
                SystemClock.ElapsedRealtime(),
                store.Load());
            var plan = LockedSessionPlan.Start(facts);
            if (!plan.Allowed)
            {
                return false;
            }

            // The record first: whatever happens next, a later start knows what to put back.
            store.Save(plan.Record);
            try
            {
                dpm.SetLockTaskPackages(admin, plan.LockTaskPackages.ToArray());
                dpm.SetLockTaskFeatures(admin, (LockTaskFeatures)plan.LockTaskFeatures);
                if (plan.UnsuspendPackage is { } unsuspend)
                {
                    dpm.SetPackagesSuspended(admin, [unsuspend], false);
                }

                ArmTick(context);
                OpenLocked(context, request);
                return true;
            }
            catch (Java.Lang.Exception exception)
            {
                Log.Warn(Tag, $"Starting the locked session failed, so it is being undone: {exception.Message}");
                ReleaseCore(context, LockedReleaseReason.Stale);
                return false;
            }
        }
    }

    /// <summary>Flyleaf's home came to the front: with a locked session running, that is Home pressed.</summary>
    public static void OnHome(Context context)
    {
        lock (Gate)
        {
            if (new LockedSessionStore(context).Load() is { } record
                && LockedSessionRules.OnHome(record, DateTimeOffset.UtcNow) is { } reason)
            {
                ReleaseCore(context, reason);
            }
        }
    }

    public static void OnTick(Context context)
    {
        lock (Gate)
        {
            if (new LockedSessionStore(context).Load() is not { } record)
            {
                return;
            }

            if (LockedSessionRules.OnTick(record, DateTimeOffset.UtcNow) is { } reason)
            {
                ReleaseCore(context, reason);
            }
            else
            {
                ArmTick(context);
            }
        }
    }

    /// <summary>
    /// A reading of a watched app's media session, before the relay decides whether to send it. The
    /// watcher passes its own sender so a closing reading for a title that was replaced goes out
    /// ahead of the reading that replaced it.
    /// </summary>
    public static void OnReading(Context context, PlaybackSessionReading reading, Action<PlaybackSessionReading> send)
    {
        lock (Gate)
        {
            var store = new LockedSessionStore(context);
            if (store.Load() is not { } record)
            {
                return;
            }

            var verdict = LockedSessionRules.OnReading(record, reading, DateTimeOffset.UtcNow);
            if (verdict.Record != record)
            {
                store.Save(verdict.Record);
            }

            if (verdict.Closing is { } closing)
            {
                send(closing);
            }

            if (verdict.Release is { } reason)
            {
                ReleaseCore(context, reason);
            }
        }
    }

    /// <summary>Process start: end a lock nobody can still be using; re-arm the clock for one that is live.</summary>
    public static void EndStaleAtStart(Context context)
    {
        lock (Gate)
        {
            if (new LockedSessionStore(context).Load() is not { } record)
            {
                return;
            }

            if (LockedSessionRules.OnProcessStart(record, DateTimeOffset.UtcNow, SystemClock.ElapsedRealtime()) is { } reason)
            {
                ReleaseCore(context, reason);
            }
            else
            {
                ArmTick(context);
            }
        }
    }

    // Idempotent: with no record it does nothing, and the record is cleared only after the policy is back.
    private static void ReleaseCore(Context context, LockedReleaseReason reason)
    {
        var store = new LockedSessionStore(context);
        var plan = LockedSessionPlan.Release(store.Load(), reason);
        if (plan.IsNothing)
        {
            return;
        }

        try
        {
            if (PolicyManager.IsDeviceOwner(context))
            {
                var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
                var admin = PolicyManager.AdminComponent(context);

                // Taking the package off the allowlist is what ends its lock task: Flyleaf cannot call
                // stopLockTask on another app's task.
                dpm.SetLockTaskPackages(admin, plan.RestoreLockTaskPackages!.ToArray());
                dpm.SetLockTaskFeatures(admin, (LockTaskFeatures)plan.RestoreLockTaskFeatures);
                if (plan.ResuspendPackage is { } resuspend)
                {
                    dpm.SetPackagesSuspended(admin, [resuspend], true);
                }
            }
        }
        catch (Java.Lang.Exception exception)
        {
            // The record stays, so the next Home press, tick or start tries again.
            Log.Warn(Tag, $"Releasing the locked session failed; it will be retried: {exception.Message}");
            return;
        }

        store.Clear();
        CancelTick(context);
        if (plan.BringForwardPackage is { } forward)
        {
            BringForward(context, forward);
        }
    }

    private static void OpenLocked(Context context, LockedOpenRequest request)
    {
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(request.Link.AbsoluteUri));
        intent.SetPackage(request.Package);
        intent.AddFlags(ActivityFlags.NewTask);
        var options = ActivityOptions.MakeBasic()!.SetLockTaskEnabled(true)!;
        context.StartActivity(intent, options.ToBundle());
    }

    private static void BringForward(Context context, string package)
    {
        try
        {
            var intent = context.PackageManager!.GetLaunchIntentForPackage(package);
            if (intent is null)
            {
                return;
            }

            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
            context.StartActivity(intent);
        }
        catch (Java.Lang.Exception exception)
        {
            Log.Info(Tag, $"Could not bring {package} forward: {exception.Message}");
        }
    }

    private static bool IsSuspended(DevicePolicyManager dpm, ComponentName admin, string package)
    {
        try
        {
            return dpm.IsPackageSuspended(admin, package);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    // The phone app must stay allowed so an incoming call can still surface over a locked task.
    private static IReadOnlyList<string> DialerPackages(Context context)
    {
        var telecom = (TelecomManager?)context.GetSystemService(Context.TelecomService);
        return new[] { telecom?.DefaultDialerPackage, telecom?.SystemDialerPackage }
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Select(package => package!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static PendingIntent TickIntent(Context context) => PendingIntent.GetBroadcast(
        context,
        TickRequestCode,
        new Intent(context, typeof(LockedSessionTickReceiver)),
        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private static void ArmTick(Context context)
    {
        var alarms = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        alarms?.SetAndAllowWhileIdle(
            AlarmType.ElapsedRealtime,
            SystemClock.ElapsedRealtime() + (long)LockedSessionRules.TickInterval.TotalMilliseconds,
            TickIntent(context));
    }

    private static void CancelTick(Context context)
    {
        var alarms = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        alarms?.Cancel(TickIntent(context));
    }
}
