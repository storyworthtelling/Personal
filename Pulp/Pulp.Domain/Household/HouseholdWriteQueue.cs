namespace Pulp.Domain.Household;

/// <summary>
/// One screen's household writes, run one after another off the caller's thread. An undo can arrive
/// while its act is still being written; run side by side it could take the household gate first, get
/// the earlier clock stamp and lose to the act it reverses. Queued, it never lands ahead of it. A write
/// that fails is handed to <paramref name="failed"/> with what it was for, the queue carries on, and
/// <paramref name="after"/> runs after every write either way so the screen re-reads what is there.
/// </summary>
public sealed class HouseholdWriteQueue(Action<string, Exception> failed, Action after)
{
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;

    /// <summary>Queues a write; the task completes when it (and everything before it) has landed.</summary>
    public Task Enqueue(string what, Func<CancellationToken, Task> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (gate)
        {
            tail = tail.ContinueWith(_ => Run(what, write), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return tail;
        }
    }

    private async Task Run(string what, Func<CancellationToken, Task> write)
    {
        try
        {
            await write(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failed(what, exception);
        }

        after();
    }
}
