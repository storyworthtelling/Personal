namespace Watch.Domain.Updates;

/// <summary>
/// One deadline for an update run, measured from when the run began (P14 release A fix round, finding 4): WorkManager
/// stops a worker ten minutes after <c>DoWork</c> starts, and the watch half of Flyleaf's run (after the phone's own
/// installs) must neither outlast that nor wait forever on a Data Layer call while it holds its gate. Nothing starts past
/// the deadline, and every wait is cut to what is left of it.
/// </summary>
public sealed record RunDeadline(DateTimeOffset Ends)
{
    /// <summary>WorkManager stops a worker this long after it starts.</summary>
    public static readonly TimeSpan WorkerCeiling = TimeSpan.FromMinutes(10);

    /// <summary>Room kept at the end, so the run can record and log what it did before the stop.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    /// <summary>The deadline of a run that began at <paramref name="started"/>: the worker's, from the start of
    /// <c>DoWork</c>, and the same for the owner's "Install from GitHub now", from the tap.</summary>
    public static RunDeadline From(DateTimeOffset started) => new(started + WorkerCeiling - Margin);

    /// <summary>What is left of the run at <paramref name="now"/>; never less than nothing.</summary>
    public TimeSpan Left(DateTimeOffset now) => now < Ends ? Ends - now : TimeSpan.Zero;

    /// <summary>Whether the deadline has come.</summary>
    public bool Passed(DateTimeOffset now) => now >= Ends;

    /// <summary>Whether a step that needs <paramref name="needs"/> may start at <paramref name="now"/>: before the
    /// deadline, with that much left.</summary>
    public bool Allows(DateTimeOffset now, TimeSpan needs) => !Passed(now) && Left(now) >= needs;

    /// <summary>How long a step may wait at <paramref name="now"/>: its own wait, cut to what is left.</summary>
    public TimeSpan Cut(DateTimeOffset now, TimeSpan wait)
    {
        var left = Left(now);
        return wait < left ? wait : left;
    }
}
