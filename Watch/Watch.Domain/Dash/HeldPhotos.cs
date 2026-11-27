namespace Watch.Domain.Dash;

/// <summary>
/// The photos the wrist has fetched (P14 B5, W-5.1.3), by attachment id, held in memory for the process only and at most
/// <see cref="Most"/>, the longest unseen going first: a conversation opened again shows them at once, and a wrist that
/// restarts asks the phone again. Nothing is written down: the phone keeps the record (A9). Shared between a screen and
/// the pool it asks from, so every access is locked.
/// </summary>
public sealed class HeldPhotos
{
    /// <summary>At most this many photos are held: a few conversations' worth, each at most <see cref="DashLimits.PhotoBytes"/>.</summary>
    public const int Most = 12;

    private readonly object gate = new();
    private readonly LinkedList<(string Id, byte[] Jpeg)> order = new();
    private readonly Dictionary<string, LinkedListNode<(string Id, byte[] Jpeg)>> byId = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (gate)
            {
                return byId.Count;
            }
        }
    }

    public bool Has(string id)
    {
        lock (gate)
        {
            return byId.ContainsKey(id);
        }
    }

    /// <summary>The photo, if it is held; seeing it makes it the last to go.</summary>
    public byte[]? Get(string id)
    {
        lock (gate)
        {
            if (!byId.TryGetValue(id, out var node))
            {
                return null;
            }
            order.Remove(node);
            order.AddLast(node);
            return node.Value.Jpeg;
        }
    }

    public void Hold(string id, byte[] jpeg)
    {
        lock (gate)
        {
            if (byId.Remove(id, out var known))
            {
                order.Remove(known);
            }
            byId[id] = order.AddLast((id, jpeg));
            while (byId.Count > Most && order.First is { } oldest)
            {
                order.RemoveFirst();
                byId.Remove(oldest.Value.Id);
            }
        }
    }
}
