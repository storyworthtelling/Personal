using Microsoft.Extensions.Logging;

namespace Index.Infrastructure.Snowball;

/// <summary>
/// How the Snowball page opens: <b>paint first from local data, import behind it.</b> The one-time history
/// import reads the owner's encrypted Drive file, and a Drive that answers slowly (or not at all, up to the
/// read cap) used to hold the whole page blank because the page awaited the import before painting. Now the
/// order is fixed here, where a test can pin it: this week's snapshot is recorded (local, quick), the page
/// paints, and only then is the import started — not awaited — and if it wrote rows the current side is
/// redrawn in place.
///
/// <para>Nothing in the background half reaches the page as an exception: a failed import or a failed
/// redraw is logged through the logger (a message and the exception — the import itself never logs the
/// payload or the passphrase), and a page that closed while the import waited (its token cancelled) ends
/// without a sound.</para>
/// </summary>
public static class SnowballOpening
{
    /// <summary>
    /// Records the week, paints, then starts the import and returns it WITHOUT waiting.
    /// The returned task completes when the import (and the redraw, if rows were written) is done; the
    /// page ignores it, a test awaits it.
    /// </summary>
    /// <param name="recordWeek">This week's snapshot writer — runs before the paint, as it always did.</param>
    /// <param name="importHistory">The history import; returns whether it wrote rows.</param>
    /// <param name="paint">The first paint, from local data.</param>
    /// <param name="repaint">Redraws the current side in place once imported rows exist.</param>
    public static async Task<Task> OpenAsync(
        Func<CancellationToken, Task> recordWeek,
        Func<CancellationToken, Task<bool>> importHistory,
        Func<Task> paint,
        Func<Task> repaint,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await recordWeek(cancellationToken);
        await paint();
        return ImportBehindThePageAsync(importHistory, repaint, logger, cancellationToken);
    }

    private static async Task ImportBehindThePageAsync(
        Func<CancellationToken, Task<bool>> importHistory,
        Func<Task> repaint,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Yield first so a synchronous import cannot run inside the caller's own paint turn.
        await Task.Yield();

        bool wroteRows;
        try
        {
            wroteRows = await importHistory(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return; // the page closed while Drive was still answering
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The Snowball history import did not complete; the page keeps what it has: {Reason}", ex.Message);
            return;
        }

        if (!wroteRows)
        {
            return;
        }

        try
        {
            await repaint();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redrawing the Snowball page after the history import did not complete: {Reason}", ex.Message);
        }
    }
}
