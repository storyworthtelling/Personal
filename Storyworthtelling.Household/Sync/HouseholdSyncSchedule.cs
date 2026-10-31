namespace Storyworthtelling.Household;

/// <summary>
/// When the next pass is due, with time passed in. Opening the app is due at once; an edit waits for
/// <see cref="Debounce"/> of quiet, so a run of ticks is one pass; a Drive that could not be reached is
/// retried with doubling backoff up to <see cref="LongestRetry"/>, and an edit does not bring that
/// forward; an unreadable file stops passes until someone asks for one (a new passphrase, Sync now).
/// Not thread-safe: the caller holds its own lock.
/// </summary>
public sealed class HouseholdSyncSchedule
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan LongestRetry = TimeSpan.FromHours(1);

    private int failures;
    private DateTimeOffset? retryAt;
    private bool blocked;

    /// <summary>When the next pass is due; null when nothing is waiting.</summary>
    public DateTimeOffset? NextDue { get; private set; }

    public void Opened(DateTimeOffset now) => Due(now);

    public void Edited(DateTimeOffset now)
    {
        if (blocked)
        {
            return;
        }

        var debounced = now + Debounce;
        NextDue = retryAt is { } retry && retry > debounced ? retry : debounced;
    }

    /// <summary>A pass begins; anything due is now its job. An edit after this schedules another.</summary>
    public void Started(DateTimeOffset now) => NextDue = null;

    public void Succeeded()
    {
        failures = 0;
        retryAt = null;
    }

    public void Failed(DateTimeOffset now)
    {
        failures++;
        var wait = TimeSpan.FromTicks(FirstRetry.Ticks * (1L << Math.Min(failures - 1, 16)));
        retryAt = now + (wait > LongestRetry ? LongestRetry : wait);
        NextDue = retryAt;
    }

    /// <summary>The file in Drive cannot be opened: no more passes until <see cref="Asked"/>.</summary>
    public void Blocked()
    {
        blocked = true;
        NextDue = null;
    }

    /// <summary>Someone asked for a pass (Sync now, a new passphrase): due at once, whatever was holding it.</summary>
    public void Asked(DateTimeOffset now)
    {
        blocked = false;
        failures = 0;
        retryAt = null;
        NextDue = now;
    }

    public bool IsDue(DateTimeOffset now) => NextDue is { } due && due <= now;

    private void Due(DateTimeOffset now)
    {
        if (!blocked)
        {
            NextDue = now;
        }
    }
}
