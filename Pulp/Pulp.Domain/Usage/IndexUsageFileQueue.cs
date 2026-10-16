namespace Pulp.Domain.Usage;

/// <summary>
/// What is waiting for the debounced write of the usage file (D320): the latest total for each touched
/// service-month, since a later total replaces an earlier one. Thread-safe, because a hand-off arrives on one
/// thread and the timer drains on another.
/// </summary>
public sealed class IndexUsageFileQueue
{
    private readonly object gate = new();
    private readonly Dictionary<(string Service, string Month), IndexUsagePayload> waiting = [];

    public bool HasWork
    {
        get
        {
            lock (gate)
            {
                return waiting.Count > 0;
            }
        }
    }

    public void Add(IEnumerable<IndexUsagePayload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        lock (gate)
        {
            foreach (var payload in payloads)
            {
                waiting[Key(payload)] = payload;
            }
        }
    }

    public IReadOnlyList<IndexUsagePayload> Drain()
    {
        lock (gate)
        {
            var taken = waiting.Values.ToList();
            waiting.Clear();
            return taken;
        }
    }

    /// <summary>Returns what a failed write took, keeping anything that arrived since: that is newer.</summary>
    public void PutBack(IEnumerable<IndexUsagePayload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        lock (gate)
        {
            foreach (var payload in payloads)
            {
                waiting.TryAdd(Key(payload), payload);
            }
        }
    }

    private static (string, string) Key(IndexUsagePayload payload) => (payload.Service.Value.ToLowerInvariant(), payload.Month.ToString());
}
