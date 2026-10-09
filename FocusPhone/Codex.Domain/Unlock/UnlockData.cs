namespace Codex.Domain.Unlock;

/// <summary>
/// Persistable snapshot of the unlock state machine. Stored in device-protected
/// storage so the countdown survives reboots and direct-boot. StoredState never
/// contains <see cref="UnlockState.Ready"/>; readiness is derived from progress. UnlockDelay is
/// null when the owner has never changed it (or the snapshot predates the setting): the default applies.
/// </summary>
public record UnlockData(
    UnlockState StoredState,
    TimeSpan AccumulatedProgress,
    DateTimeOffset LastObservedUtc,
    DateTimeOffset? AdminExpiresAtUtc,
    TimeSpan? UnlockDelay = null);
