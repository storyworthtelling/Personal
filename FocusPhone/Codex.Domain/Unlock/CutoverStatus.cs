namespace Codex.Domain.Unlock;

/// <summary>What the admin panel should show for the one irreversible cutover step.</summary>
public enum CutoverPrompt
{
    /// <summary>A finalize countdown is live — show the seconds + Cancel.</summary>
    Countdown,

    /// <summary>Debugging is still enabled — offer the finalize-cutover button.</summary>
    Finalize,

    /// <summary>Debugging is already locked down — the cutover is done, nothing left to finalize.</summary>
    Complete,
}

/// <summary>
/// Decides whether the admin panel still needs the "Finalize cutover" button. Its one job is to
/// disable debugging permanently, so once <c>debuggingLockedDown</c> is set there is nothing left
/// to do and the button is retired for a done-state line — the button should only be visible while
/// there is still work to do. A live countdown wins over both (the finalize is in flight).
/// </summary>
public static class CutoverStatus
{
    public static CutoverPrompt Decide(bool debuggingLockedDown, bool countdownRunning) =>
        countdownRunning ? CutoverPrompt.Countdown
        : debuggingLockedDown ? CutoverPrompt.Complete
        : CutoverPrompt.Finalize;
}
