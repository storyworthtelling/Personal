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
        try
        {
            var intent = new Intent(IndexHandoff.Action);
            intent.SetComponent(new ComponentName(IndexHandoff.Package, IndexHandoff.Receiver));
            foreach (var (key, value) in IndexHandoff.Extras(payload))
            {
                intent.PutExtra(key, value);
            }

            context.SendBroadcast(intent, IndexHandoff.Permission);
        }
        catch (Exception e)
        {
            // Dropped on purpose, and quietly: Index not being installed is the ordinary
            // case today, not a fault worth telling the owner about.
            Android.Util.Log.Info("Post", $"Balance handoff to Index did not land: {e.Message}");
        }
    }
}
