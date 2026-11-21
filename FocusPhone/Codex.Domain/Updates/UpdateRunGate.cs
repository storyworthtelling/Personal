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

    /// <summary>
    /// Runs <paramref name="run"/> if the gate comes free within <paramref name="wait"/>; otherwise runs nothing (P14
    /// release A fix round): a run with a deadline never waits behind another past it. A wait of nothing or less tries
    /// the gate once.
    /// </summary>
    /// <returns>Whether it ran.</returns>
    public async Task<bool> TryRunAsync(Func<Task> run, TimeSpan wait)
    {
        if (!await gate.WaitAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero))
        {
            return false;
        }
        try
        {
            await run();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }
}
