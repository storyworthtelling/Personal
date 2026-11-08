namespace Codex.Domain.Updates;

/// <summary>
/// Lets one update run at a time. The six-hourly worker and the admin "Install from GitHub now"
/// can overlap, and two runs share one cache file per release asset and open install sessions
/// for the same package; the second then fails with a duplicate pending install or a missing file.
/// A run that arrives while another is going waits for it, then runs on what that run left.
/// </summary>
public sealed class UpdateRunGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> run)
    {
        await gate.WaitAsync();
        try
        {
            return await run();
        }
        finally
        {
            gate.Release();
        }
    }
}
