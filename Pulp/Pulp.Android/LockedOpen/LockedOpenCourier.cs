using Android.App;
using Android.Content;
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
    /// True when a broadcast was handed to the system; false when none could be sent, so the caller
    /// falls back to the plain hand-off. <c>SendBroadcast</c> cannot say whether anyone received it:
    /// Flyleaf refusing the request is seen only as the landing check's quiet "may not have opened" (D286).
    /// </summary>
    public static bool Send(Context context, string package, Uri link)
    {
        try
        {
            var intent = new Intent(LockedOpenHandOff.Action);
            intent.SetComponent(new ComponentName(LockedOpenHandOff.Package, LockedOpenHandOff.Receiver));
            foreach (var (key, value) in LockedOpenHandOff.Extras(package, link))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, LockedOpenHandOff.Permission);
            return true;
        }
        catch (Exception e)
        {
            Android.Util.Log.Info("Pulp", $"Locked open request to Flyleaf did not go: {e.Message}");
            return false;
        }
    }
}
