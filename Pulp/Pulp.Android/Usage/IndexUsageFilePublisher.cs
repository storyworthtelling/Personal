using Pulp.Domain.Usage;
using Pulp.Infrastructure.Usage;

namespace Pulp.Usage;

/// <summary>
/// Leaves the month totals in Drive for an Index install that Pulp never ran beside (D320), after the broadcast
/// has gone to the local receiver. The broadcast reaches only a phone both apps live on; this carries the same
/// numbers through the household's passphrase-protected file. Debounced, because a run of hand-offs is one
/// write, and best effort: no passphrase, no network or a file this phone cannot open (never written over, G14)
/// only waits for the next hand-off, which sends the month's totals again.
/// </summary>
internal sealed class IndexUsageFilePublisher
{
    /// <summary>How long a hand-off waits for company before the file is written.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);

    private readonly IndexUsageFileQueue queue;
    private readonly IndexUsageFileFlusher flusher;
    private readonly object gate = new();
    private CancellationTokenSource? pending;

    public IndexUsageFilePublisher(DriveIndexUsageStore store, Action<string, Exception> warn)
    {
        queue = new IndexUsageFileQueue();
        flusher = new IndexUsageFileFlusher(queue, store.PublishAsync, warn);
    }

    /// <summary>Queues the service-months a write touched (zero for one whose last hand-off was undone) and restarts the wait.</summary>
    public void Queue(IEnumerable<IndexUsagePayload> payloads)
    {
        queue.Add(payloads);
        CancellationTokenSource timer;
        lock (gate)
        {
            pending?.Cancel();
            timer = pending = new CancellationTokenSource();
        }

        _ = Task.Run(() => flusher.FlushAfterAsync(Debounce, Task.Delay, timer.Token));
    }
}
