namespace Pulp.Domain.Usage;

/// <summary>
/// The decisions behind the debounced write of the usage file (D320, D359), apart from Drive. A cancel while
/// waiting out the debounce means a newer hand-off will flush everything, so the queue is left alone. Once the
/// queue has been drained, any failure — including a <see cref="TaskCanceledException"/> from the Drive
/// client's own timeout, which is a cancellation in type only — puts the payloads back and warns. Flushes are
/// serialised: a second read-merge-write on the same file could lose a service-month or create the file twice.
/// </summary>
public sealed class IndexUsageFileFlusher(
    IndexUsageFileQueue queue,
    Func<IReadOnlyList<IndexUsagePayload>, CancellationToken, Task> publish,
    Action<string, Exception> warn)
{
    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    public async Task FlushAfterAsync(TimeSpan debounce, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken debounceToken)
    {
        try
        {
            await delay(debounce, debounceToken);
            await oneAtATime.WaitAsync(debounceToken);
        }
        catch (OperationCanceledException) when (debounceToken.IsCancellationRequested)
        {
            // A newer hand-off restarted the wait and will flush everything; nothing has been drained.
            return;
        }

        try
        {
            var taken = queue.Drain();
            if (taken.Count == 0)
            {
                return;
            }

            try
            {
                await publish(taken, CancellationToken.None);
            }
            catch (Exception exception)
            {
                queue.PutBack(taken);
                warn("The usage file in Drive was not written; it waits for the next hand-off.", exception);
            }
        }
        finally
        {
            oneAtATime.Release();
        }
    }
}
