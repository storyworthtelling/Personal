using Android.App;
using Android.Content;
using Pulp.Domain.Core;
using Pulp.Domain.Hidden;
using Pulp.Domain.Services;

// Flyleaf declares RECEIVE_HIDDEN at signature level; Pulp must hold it for the broadcast to land.
[assembly: UsesPermission(HiddenAppsHandOff.Permission)]

namespace Pulp.Hidden;

/// <summary>
/// Sends Flyleaf the set of streaming apps to hide (D92). An explicit broadcast naming Flyleaf's
/// receiver, under the signature permission, so nothing else can intercept the set and nothing else
/// can send one. Not a fallback and not a queue (D93): a missing Flyleaf (her unrestricted phone,
/// D94) drops the send, and the call is cheap to repeat whenever the ticked services change.
/// </summary>
internal static class HiddenAppsCourier
{
    /// <summary>
    /// True when a broadcast was handed to the system. False means none could be sent (the intent could not be
    /// built or the call threw), so the caller may try again. A broadcast to a Flyleaf that is not installed
    /// reports success all the same: <c>SendBroadcast</c> cannot say whether anyone received it.
    /// </summary>
    public static bool Send(Context context, ServiceCatalogue catalogue, IEnumerable<ServiceId> ticked)
    {
        try
        {
            var packages = HiddenAppsHandOff.PackagesFor(catalogue, ticked);
            var intent = new Intent(HiddenAppsHandOff.Action);
            intent.SetComponent(new ComponentName(HiddenAppsHandOff.Package, HiddenAppsHandOff.Receiver));
            foreach (var (key, value) in HiddenAppsHandOff.Extras(packages))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, HiddenAppsHandOff.Permission);
            return true;
        }
        catch (Exception e)
        {
            // Dropped on purpose: Flyleaf not being on this phone is the ordinary case on hers.
            Android.Util.Log.Info("Pulp", $"Hidden-apps hand-off to Flyleaf did not land: {e.Message}");
            return false;
        }
    }
}
