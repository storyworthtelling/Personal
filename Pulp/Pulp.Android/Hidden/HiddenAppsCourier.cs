using Codex.Ui;
using Storyworthtelling.Diagnostics;
using Android.App;
using Android.Content;
using Pulp.Domain.Core;
using Pulp.Domain.Hidden;
using Pulp.Domain.Services;

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
    /// True when a broadcast was handed to the system, or when Flyleaf is not on this phone, which is the
    /// ordinary case on hers and nothing to try again. False means a send was due and did not happen (Pulp does
    /// not hold the permission, the intent could not be built or the call threw), so the caller may try again.
    /// </summary>
    public static bool Send(Context context, ServiceCatalogue catalogue, IEnumerable<ServiceId> ticked)
    {
        var before = ContractCourier.Before(context, HiddenAppsHandOff.Package, HiddenAppsHandOff.Permission);
        if (before != ContractSendOutcome.Sent)
        {
            HeadLog.Info("Pulp", ContractBroadcast.SendLine(Contract, before));
            return before == ContractSendOutcome.ReceiverAbsent;
        }

        try
        {
            var packages = HiddenAppsHandOff.PackagesFor(catalogue, ticked);
            var intent = new Intent(HiddenAppsHandOff.Action);
            intent.SetComponent(new ComponentName(HiddenAppsHandOff.Package, HiddenAppsHandOff.Receiver));
            intent.PutExtra(ContractBroadcast.VersionExtra, ContractBroadcast.Version);
            foreach (var (key, value) in HiddenAppsHandOff.Extras(packages))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, HiddenAppsHandOff.Permission);
            HeadLog.Info("Pulp", ContractBroadcast.SendLine(Contract, ContractSendOutcome.Sent));
            return true;
        }
        catch (Exception e)
        {
            HeadLog.Info("Pulp", ContractBroadcast.SendLine(Contract, ContractSendOutcome.Failed, e.GetType().Name));
            return false;
        }
    }

    private const string Contract = "hidden-apps";
}
