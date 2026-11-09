using Codex.Ui;
using Storyworthtelling.Diagnostics;
using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Carries a balance across to Index. Post parses, Index consumes.
///
/// An <b>explicit</b> intent naming Index's receiver, guarded by a signature-level
/// permission Index declares. Both halves matter: naming the component means nothing else
/// can intercept the balance, and the signature permission means nothing else can send
/// one. Without the second, any app on the phone could feed Index fabricated balances —
/// and it costs nothing, because every head already shares one keystore for the update
/// channel.
///
/// <b>No outbox.</b> If Index is not installed, or the send fails, the balance is dropped
/// and nothing is retried. Index sees the same email on a later sync, so the case
/// self-heals; a durable retry queue would be real machinery to maintain for something
/// that fixes itself.
/// </summary>
internal static class IndexCourier
{
    public static void Send(Context context, IndexBalance payload)
    {
        var before = ContractCourier.Before(context, IndexHandoff.Package, IndexHandoff.Permission);
        if (before != ContractSendOutcome.Sent)
        {
            // Index not being installed is the ordinary case today, not a fault worth telling the owner about.
            HeadLog.Info("Post", ContractBroadcast.SendLine(Contract, before));
            return;
        }

        try
        {
            var intent = new Intent(IndexHandoff.Action);
            intent.PutExtra(ContractBroadcast.VersionExtra, ContractBroadcast.Version);
            intent.SetComponent(new ComponentName(IndexHandoff.Package, IndexHandoff.Receiver));
            foreach (var (key, value) in IndexHandoff.Extras(payload))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, IndexHandoff.Permission);
            HeadLog.Info("Post", ContractBroadcast.SendLine(Contract, ContractSendOutcome.Sent));
        }
        catch (Exception e)
        {
            // Dropped on purpose; the type name only, since a message can carry the payload.
            HeadLog.Info("Post", ContractBroadcast.SendLine(Contract, ContractSendOutcome.Failed, e.GetType().Name));
        }
    }

    private const string Contract = "balance";
}
