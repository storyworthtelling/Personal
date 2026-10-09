namespace Codex.Domain.Unlock;

/// <summary>
/// The time-delay admin unlock (a 6-hour cooldown, owner-chosen). Progress accumulates only
/// while the wall clock moves forward; a backwards clock jump pauses the countdown (fail-closed)
/// and immediately ends an active admin session. All state is recomputed from persisted timestamps
/// on every observation, so alarms and in-memory timers are never load-bearing.
/// </summary>
public class UnlockStateMachine
{
    public static readonly TimeSpan DefaultUnlockDelay = TimeSpan.FromHours(6);
    public static readonly TimeSpan MinimumUnlockDelay = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaximumUnlockDelay = TimeSpan.FromDays(7);
    /// <summary>The delays the admin screen offers, in whole hours. Includes <see cref="DefaultUnlockDelay"/>.</summary>
    public static readonly int[] DelayChoices = [1, 2, 3, 6, 12, 24, 48, 72, 168];
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
            UnlockDelay = DefaultUnlockDelay;
        }
        else
        {
            storedState = persisted.StoredState;
            accumulatedProgress = persisted.AccumulatedProgress;
            lastObservedUtc = persisted.LastObservedUtc;
            adminExpiresAtUtc = persisted.AdminExpiresAtUtc;
            UnlockDelay = persisted.UnlockDelay ?? DefaultUnlockDelay;
        }
    }

    /// <summary>
    /// How long the owner waits between requesting admin access and being allowed in. Read when a
    /// request starts, so it can only be changed while Locked (see <see cref="SetDelay"/>).
    /// </summary>
    public TimeSpan UnlockDelay { get; private set; }

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

    /// <summary>
    /// Changes the delay used by future requests. Refused while a request is in flight (Pending or
    /// Ready): changing it then would shorten or lengthen a countdown the owner is already waiting out.
    /// Allowed while Locked and while admin mode is open, since no request depends on it then.
    /// </summary>
    public void SetDelay(TimeSpan delay)
    {
        Touch();
        if (delay < MinimumUnlockDelay || delay > MaximumUnlockDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(delay),
                $"The admin unlock delay must be between {MinimumUnlockDelay.TotalHours:0} hour and {MaximumUnlockDelay.TotalDays:0} days; got {delay.TotalMinutes:0} minutes.");
        }
        var state = EffectiveState();
        if (state is UnlockState.Pending or UnlockState.Ready)
        {
            throw new InvalidOperationException(
                $"Cannot change the admin unlock delay while in state {state}; a request is already waiting on it.");
        }
        UnlockDelay = delay;
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
        return new UnlockData(storedState, accumulatedProgress, lastObservedUtc, adminExpiresAtUtc, UnlockDelay);
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
