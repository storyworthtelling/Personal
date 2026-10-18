namespace Constitution.Infrastructure.Backup;

/// <summary>
/// Decides when the Body backup is due, so a burst of readings is one write to Drive and not a dozen.
/// It holds no clock and starts no timer: the caller says when a change happened and asks whether the
/// time has come, and tells it the time each time, so every rule is a sum on a date.
///
/// <list type="bullet">
/// <item>A change makes the backup due once <see cref="QuietWindow"/> has passed with no further change.</item>
/// <item>A stream of changes cannot hold the backup off for ever: it is due <see cref="LongestWait"/>
/// after the first change in the burst, whatever came after.</item>
/// <item>Taking the pending backup clears it, so a change that lands while the backup is running is
/// still pending afterwards; a backup that did not happen puts itself back with <see cref="Changed"/>.</item>
/// </list>
///
/// <para>Safe to call from any thread; every member takes one lock.</para>
/// </summary>
public sealed class BodyBackupDebouncer
{
    /// <summary>How long the readings must stay quiet before the backup is written.</summary>
    public static readonly TimeSpan QuietWindow = TimeSpan.FromMinutes(2);

    /// <summary>The longest a pending backup is held off by a stream of changes.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(15);

    private readonly object gate = new();
    private bool pending;
    private DateTimeOffset firstChange;
    private DateTimeOffset lastChange;

    /// <summary>True when a change has been noted and not yet taken.</summary>
    public bool HasPending
    {
        get
        {
            lock (gate)
            {
                return pending;
            }
        }
    }

    /// <summary>When the pending backup falls due, or null when nothing is pending.</summary>
    public DateTimeOffset? DueAt
    {
        get
        {
            lock (gate)
            {
                return DueAtLocked();
            }
        }
    }

    /// <summary>Notes that Body history changed at <paramref name="now"/>.</summary>
    public void Changed(DateTimeOffset now)
    {
        lock (gate)
        {
            if (!pending)
            {
                pending = true;
                firstChange = now;
                lastChange = now;
                return;
            }

            if (now > lastChange)
            {
                lastChange = now;
            }
        }
    }

    /// <summary>Whether a pending backup has fallen due by <paramref name="now"/>.</summary>
    public bool IsDue(DateTimeOffset now)
    {
        lock (gate)
        {
            return DueAtLocked() is { } due && now >= due;
        }
    }

    /// <summary>If the backup is due, takes it (clearing it) and says so; otherwise leaves it pending.</summary>
    public bool TakeIfDue(DateTimeOffset now)
    {
        lock (gate)
        {
            if (DueAtLocked() is not { } due || now < due)
            {
                return false;
            }

            pending = false;
            return true;
        }
    }

    /// <summary>Takes a pending backup whether or not it is due: the flush on demand, for when the app is leaving.</summary>
    public bool TakeNow()
    {
        lock (gate)
        {
            var wasPending = pending;
            pending = false;
            return wasPending;
        }
    }

    private DateTimeOffset? DueAtLocked()
    {
        if (!pending)
        {
            return null;
        }

        var quiet = lastChange + QuietWindow;
        var ceiling = firstChange + LongestWait;
        return quiet < ceiling ? quiet : ceiling;
    }
}
