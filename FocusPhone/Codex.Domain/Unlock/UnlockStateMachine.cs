namespace Codex.Domain.Unlock;

/// <summary>
/// The 72-hour time-delay admin unlock. Progress accumulates only while the
/// wall clock moves forward; a backwards clock jump pauses the countdown
/// (fail-closed) and immediately ends an active admin session. All state is
/// recomputed from persisted timestamps on every observation, so alarms and
/// in-memory timers are never load-bearing.
/// </summary>
public class UnlockStateMachine
{
    public static readonly TimeSpan UnlockDelay = TimeSpan.FromHours(72);
    public static readonly TimeSpan AdminDuration = TimeSpan.FromMinutes(60);

    private readonly IClock clock;
    private UnlockState storedState;
    private TimeSpan accumulatedProgress;
    private DateTimeOffset lastObservedUtc;
    private DateTimeOffset? adminExpiresAtUtc;

    public UnlockStateMachine(IClock clock, UnlockData? persisted = null)
    {
        this.clock = clock;
        if (persisted is null)
        {
            storedState = UnlockState.Locked;
            accumulatedProgress = TimeSpan.Zero;
            lastObservedUtc = clock.UtcNow;
        }
        else
        {
            storedState = persisted.StoredState;
            accumulatedProgress = persisted.AccumulatedProgress;
            lastObservedUtc = persisted.LastObservedUtc;
            adminExpiresAtUtc = persisted.AdminExpiresAtUtc;
        }
    }

    public UnlockState CurrentState
    {
        get
        {
            Touch();
            return EffectiveState();
        }
    }

    public TimeSpan? RemainingDelay
    {
        get
        {
            Touch();
            if (storedState != UnlockState.Pending || accumulatedProgress >= UnlockDelay)
            {
                return null;
            }
            return UnlockDelay - accumulatedProgress;
        }
    }

    public void RequestUnlock()
    {
        Touch();
        if (storedState != UnlockState.Locked)
        {
            throw new InvalidOperationException(
                $"Cannot request an unlock while in state {EffectiveState()}; only a Locked machine accepts a new request.");
        }
        storedState = UnlockState.Pending;
        accumulatedProgress = TimeSpan.Zero;
    }

    public void CancelRequest()
    {
        Touch();
        if (storedState != UnlockState.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot cancel an unlock request while in state {EffectiveState()}; there is no pending request.");
        }
        storedState = UnlockState.Locked;
        accumulatedProgress = TimeSpan.Zero;
    }

    public void EnterAdminMode()
    {
        Touch();
        if (EffectiveState() != UnlockState.Ready)
        {
            throw new InvalidOperationException(
                $"Cannot enter admin mode while in state {EffectiveState()}; the {UnlockDelay.TotalHours}-hour delay must fully elapse first.");
        }
        storedState = UnlockState.AdminActive;
        adminExpiresAtUtc = clock.UtcNow + AdminDuration;
    }

    public void Relock()
    {
        storedState = UnlockState.Locked;
        accumulatedProgress = TimeSpan.Zero;
        adminExpiresAtUtc = null;
        lastObservedUtc = clock.UtcNow;
    }

    public UnlockData Export()
    {
        Touch();
        return new UnlockData(storedState, accumulatedProgress, lastObservedUtc, adminExpiresAtUtc);
    }

    private void Touch()
    {
        var now = clock.UtcNow;
        var delta = now - lastObservedUtc;
        lastObservedUtc = now;

        switch (storedState)
        {
            case UnlockState.Pending when delta > TimeSpan.Zero:
                accumulatedProgress += delta;
                break;
            case UnlockState.AdminActive when delta < TimeSpan.Zero || now >= adminExpiresAtUtc:
                storedState = UnlockState.Locked;
                accumulatedProgress = TimeSpan.Zero;
                adminExpiresAtUtc = null;
                break;
        }
    }

    private UnlockState EffectiveState() =>
        storedState == UnlockState.Pending && accumulatedProgress >= UnlockDelay
            ? UnlockState.Ready
            : storedState;
}
