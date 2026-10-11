using Android.Content;
using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Domain.Usage;

namespace Pulp.Usage;

/// <summary>
/// Carries the usage count across to Index (D90). An explicit intent naming Index's receiver, sent
/// under a signature permission: naming the component means nothing else can intercept it, and the
/// permission means nothing else can send one. No outbox: if Index is not installed or a send fails,
/// the rows are dropped, and the month's totals go again with the next hand-off.
/// </summary>
internal static class IndexUsageCourier
{
    /// <summary>
    /// Tallies the recorded hand-offs and sends one broadcast for each touched service-month, not all history.
    /// A touched month left with no hand-offs goes as an explicit zero so Index clears it. Wiring: call after a
    /// hand-off is recorded or removed.
    /// </summary>
    public static void Publish(
        Context context, IEnumerable<UsageHandOff> handOffs, ServiceCatalogue catalogue, IEnumerable<(ServiceId Service, UsageMonth Month)> touched)
    {
        var rows = UsageCount.Tally(handOffs, []);
        foreach (var payload in IndexUsagePayload.ForTouched(rows, touched, catalogue))
        {
            Send(context, payload, catalogue);
        }
    }

    public static void Send(Context context, IndexUsagePayload payload, ServiceCatalogue catalogue)
    {
        try
        {
            var intent = new Intent(IndexUsageContract.Action);
            intent.SetComponent(new ComponentName(IndexUsageContract.Package, IndexUsageContract.Receiver));
            foreach (var (key, value) in IndexUsageContract.Extras(payload, catalogue))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, IndexUsageContract.Permission);
        }
        catch (Exception e)
        {
            // Dropped on purpose and quietly: Index not being installed is the ordinary case.
            Android.Util.Log.Info("Pulp", $"Usage hand-off to Index did not land: {e.Message}");
        }
    }
}
