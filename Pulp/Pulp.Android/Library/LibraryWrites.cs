using Pulp.App;

namespace Pulp.Library;

/// <summary>
/// The household writes a Library page makes, run one after another off the UI thread. An undo can
/// arrive while its act is still being written, so each write waits for the one before it — an undo
/// never lands ahead of what it reverses. A write that fails is logged to Diagnostics with what it was
/// for, and <paramref name="after"/> still runs so the page re-reads what is really there.
/// </summary>
internal sealed class LibraryWrites(PulpServices services, Action after)
{
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;

    public void Enqueue(string what, Func<CancellationToken, Task> write)
    {
        lock (gate)
        {
            tail = tail.ContinueWith(_ => Run(what, write), TaskScheduler.Default).Unwrap();
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
            services.Diagnostics.Warn("library", $"Could not {what}; the page shows what the household file holds.", exception);
        }

        after();
    }
}
