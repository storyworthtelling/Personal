namespace Codex.Domain.Unlock;

/// <summary>
/// The inline countdown guarding the one irreversible action (DESIGN.md §7):
/// undo-first even for finalize-cutover. Started visibly, cancellable at any
/// tick, and only expired when a live countdown has fully elapsed — an
/// abandoned countdown (screen left) is cancelled, never resumed.
/// </summary>
public sealed class InlineCountdown
{
    private readonly IClock clock;
    private readonly TimeSpan duration;
    private DateTimeOffset? startedAtUtc;

    public InlineCountdown(IClock clock, TimeSpan duration)
    {
        this.clock = clock;
        this.duration = duration;
    }

    public bool IsRunning => startedAtUtc is not null;

    public void Start() => startedAtUtc = clock.UtcNow;

    public void Cancel() => startedAtUtc = null;

    public TimeSpan Remaining
    {
        get
        {
            if (startedAtUtc is not { } started)
            {
                return duration;
            }
            var remaining = started + duration - clock.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>True exactly once, when a running countdown has fully elapsed.</summary>
    public bool HasExpired => startedAtUtc is { } started && clock.UtcNow >= started + duration;
}
