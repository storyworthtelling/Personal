namespace Index.Infrastructure.Usage;

/// <summary>
/// When Pulp's usage file may be read again. A read that worked waits half an hour, because the page asks on every
/// appearance; one that failed waits two minutes, not half an hour, so a dropped connection or a passphrase just
/// entered is retried soon rather than leaving the line under a charge missing for the rest of the session. A read
/// already under way is not started a second time. Time is passed in.
/// </summary>
public sealed class PulpUsageThrottle
{
    public static readonly TimeSpan AfterSuccess = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(2);

    private readonly object gate = new();
    private DateTime nextAllowed = DateTime.MinValue;
    private bool running;

    /// <summary>True when a read may start now; the caller then owes an <see cref="End"/>.</summary>
    public bool TryBegin(DateTime now)
    {
        lock (gate)
        {
            if (running || now < nextAllowed)
            {
                return false;
            }

            running = true;
            return true;
        }
    }

    public void End(DateTime now, bool succeeded)
    {
        lock (gate)
        {
            running = false;
            nextAllowed = now + (succeeded ? AfterSuccess : AfterFailure);
        }
    }

    /// <summary>A file read and kept, or none there at all, is a read that worked; every other status is one to ask for again soon.</summary>
    public static bool Worked(PulpUsageReadStatus status) => status is PulpUsageReadStatus.Loaded or PulpUsageReadStatus.Absent;
}
