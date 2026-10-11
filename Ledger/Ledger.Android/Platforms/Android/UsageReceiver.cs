using Android.App;
using Android.Content;
using Index.Domain.Identity;
using Index.Domain.Usage;
using Microsoft.Extensions.Logging;

namespace Index.Android.Platforms.Android;

/// <summary>
/// Pulp's usage count (Pulp D90, D269–D271), the <see cref="BalanceReceiver"/> pattern again: an explicit
/// broadcast naming this class, guarded by a signature permission this app declares in its manifest.
/// The package, class, action, permission and field names are Pulp's contract
/// (<c>Pulp/Pulp.Domain/Usage/IndexUsageContract.cs</c>), and Guard G16 (<c>PulpUsageContractTests</c>)
/// reads that file directly to hold them equal.
///
/// <para>A receipt only updates the stored ledger of received months; the line under a matching charge
/// is drawn from it (<see cref="PulpUsageLine"/>). A receipt that does not parse is logged and dropped:
/// usage is a convenience, and a guessed count would be a number nobody can vouch for.</para>
/// </summary>
[BroadcastReceiver(
    Name = IndexIdentity.ApplicationId + ".UsageReceiver",
    Exported = true,
    Enabled = true,
    Permission = IndexIdentity.ApplicationId + ".RECEIVE_USAGE")]
[IntentFilter([UsageReceiver.HandoffAction])]
public class UsageReceiver : BroadcastReceiver
{
    public const string HandoffAction = IndexIdentity.ApplicationId + ".action.USAGE";
    public const string ServiceExtra = "service";
    public const string MonthExtra = "month";
    public const string CountExtra = "count";
    public const string LastOpenedExtra = "lastOpened";
    public const string MerchantsExtra = "merchants";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != HandoffAction)
        {
            return;
        }

        try
        {
            if (!PulpUsageReceipt.TryParse(key => intent.GetStringExtra(key), out var receipt) || receipt is null)
            {
                Log("received a usage receipt that did not parse; dropped");
                return;
            }

            PulpUsageStore.Save(PulpUsageStore.Load().Apply(receipt));
        }
        catch (Exception e)
        {
            // Never throw out of a receiver: it shares a process with the ones that matter more.
            var logger = IPlatformApplication.Current?.Services.GetService<ILogger<UsageReceiver>>();
            logger?.LogWarning(e, "UsageReceiver: failed to store a usage receipt");
        }
    }

    private static void Log(string reason)
    {
        var logger = IPlatformApplication.Current?.Services.GetService<ILogger<UsageReceiver>>();
        logger?.LogWarning("UsageReceiver: {Reason}", reason);
    }
}

/// <summary>The ledger of received months, kept in Preferences as one JSON value. Receiver and page share the lock.</summary>
public static class PulpUsageStore
{
    private const string Key = "pulp-usage";
    private static readonly object Gate = new();

    public static PulpUsageLedger Load()
    {
        lock (Gate)
        {
            return PulpUsageCodec.Decode(Preferences.Get(Key, string.Empty));
        }
    }

    public static void Save(PulpUsageLedger ledger)
    {
        lock (Gate)
        {
            Preferences.Set(Key, PulpUsageCodec.Encode(ledger));
        }
    }
}
