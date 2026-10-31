namespace Storyworthtelling.Household;

/// <summary>
/// Merges two copies of the household: per key, the record that <see cref="HouseholdRecord.Outranks">
/// outranks</see> the other wins (last writer wins, ties broken by writer id, a delete over a write).
/// Because that ranking is a total order, the merge is commutative, associative and idempotent: the
/// two installs converge on one household whatever order they read each other in
/// (finding §2.7). <c>Storyworthtelling.Household.Tests/HouseholdConvergenceTests</c> is G11.
/// </summary>
public static class HouseholdMerge
{
    public static HouseholdDocument Merge(HouseholdDocument left, HouseholdDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return right.Records.Aggregate(left, (merged, record) => merged.With(record));
    }
}

/// <summary>What the publisher does after re-reading the file.</summary>
public enum PublishAction
{
    /// <summary>The file holds everything this phone has; nothing left to do.</summary>
    Done,

    /// <summary>Another writer's write replaced some of ours between our write and the re-read: write the merge again.</summary>
    WriteAgain,

    /// <summary>Still lost after the second write. The phone keeps the merge and the next pass carries it.</summary>
    LeaveForNextPass,
}

/// <summary>One step of the publish cycle: what to do, and the document the phone now holds (and writes, if writing).</summary>
public sealed record PublishStep(PublishAction Action, HouseholdDocument Document);

/// <summary>
/// The read → merge → write → re-read → re-merge-once cycle of finding §2.7, as pure decisions.
/// Drive v3 has no conditional write, so two phones can race between read and write and the later
/// write silently replaces the earlier one. Re-reading catches that; writing the merge once more
/// repairs it in the common case; and a write that loses even then is not lost, because the phone
/// keeps it and the next pass carries it. The store does the I/O and asks this what to do.
/// </summary>
public static class HouseholdPublish
{
    /// <summary>At most two writes per pass, so two phones racing cannot keep each other looping.</summary>
    public const int MaxWrites = 2;

    /// <summary>The document to write on the first attempt: this phone's copy merged with what it just read.</summary>
    public static HouseholdDocument FirstWrite(HouseholdDocument local, HouseholdDocument read) =>
        HouseholdMerge.Merge(local, read);

    /// <summary>
    /// Decides the next step after re-reading. <paramref name="writesSoFar"/> counts this pass's
    /// writes. The returned document is always what the phone should hold from now on — its own
    /// copy merged with the re-read — whether or not it is written again.
    /// </summary>
    public static PublishStep AfterReread(HouseholdDocument local, HouseholdDocument reread, int writesSoFar)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(writesSoFar, 1);
        var merged = HouseholdMerge.Merge(local, reread);
        if (merged.ContentEquals(reread))
        {
            return new PublishStep(PublishAction.Done, merged);
        }

        return writesSoFar < MaxWrites
            ? new PublishStep(PublishAction.WriteAgain, merged)
            : new PublishStep(PublishAction.LeaveForNextPass, merged);
    }
}
