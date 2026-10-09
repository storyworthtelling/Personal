namespace Pulp.Domain.Household;

/// <summary>
/// A hybrid-logical-clock stamp: the wall time in milliseconds the clock had reached, and a counter
/// that orders events inside one millisecond (or after the clock was pulled forward by a stamp read
/// from another device). Ordered by wall time, then counter. The writer is not part of the stamp;
/// <see cref="HouseholdRecord"/> breaks ties between writers itself.
/// </summary>
public readonly record struct HlcTimestamp : IComparable<HlcTimestamp>
{
    public HlcTimestamp(long wallMillis, int counter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wallMillis);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        WallMillis = wallMillis;
        Counter = counter;
    }

    public static HlcTimestamp Zero => default;

    public long WallMillis { get; }

    public int Counter { get; }

    public int CompareTo(HlcTimestamp other)
    {
        var byWall = WallMillis.CompareTo(other.WallMillis);
        return byWall != 0 ? byWall : Counter.CompareTo(other.Counter);
    }

    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;

    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;

    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;

    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    public override string ToString() => WallMillis + "." + Counter;
}

/// <summary>
/// One device's hybrid logical clock (Kulkarni et al., 2014). It follows the phone's own clock while
/// that clock is ahead, never runs backwards when the phone's clock steps back, and — the part that
/// matters for the household file — is pulled past any stamp it has <see cref="Witness">read</see>,
/// so an edit made after reading the file always outranks what was read, however slow this phone's
/// clock is.
///
/// <para>Physical time is passed in rather than read, so the rule is testable and the caller owns
/// the clock. The caller persists <see cref="Last"/> between runs and resumes from it: a fresh clock
/// on a phone whose time has been set back would otherwise stamp edits older than its own last
/// ones.</para>
///
/// <para>Not bounded: a device whose clock is wildly ahead stamps records that win until real time
/// catches up. A drift bound would need a policy for what to do with an over-bound stamp read from
/// the file (refuse it and the households diverge), and none was asked for.</para>
/// </summary>
public sealed class HybridLogicalClock
{
    public HybridLogicalClock()
        : this(HlcTimestamp.Zero)
    {
    }

    public HybridLogicalClock(HlcTimestamp last) => Last = last;

    /// <summary>The latest stamp this clock has issued or witnessed.</summary>
    public HlcTimestamp Last { get; private set; }

    /// <summary>A stamp for a local event (an edit) at the given physical time.</summary>
    public HlcTimestamp Now(long physicalMillis)
    {
        Last = physicalMillis > Last.WallMillis
            ? new HlcTimestamp(physicalMillis, 0)
            : new HlcTimestamp(Last.WallMillis, Last.Counter + 1);
        return Last;
    }

    /// <summary>Moves the clock past a stamp read from elsewhere — the household file's newest record.</summary>
    public HlcTimestamp Witness(HlcTimestamp remote, long physicalMillis)
    {
        var wall = Math.Max(Math.Max(Last.WallMillis, remote.WallMillis), physicalMillis);
        int counter;
        if (wall == Last.WallMillis && wall == remote.WallMillis)
        {
            counter = Math.Max(Last.Counter, remote.Counter) + 1;
        }
        else if (wall == Last.WallMillis)
        {
            counter = Last.Counter + 1;
        }
        else if (wall == remote.WallMillis)
        {
            counter = remote.Counter + 1;
        }
        else
        {
            counter = 0;
        }

        Last = new HlcTimestamp(wall, counter);
        return Last;
    }
}
