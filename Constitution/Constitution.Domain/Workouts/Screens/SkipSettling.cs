namespace Constitution.Domain.Workouts.Screens;

/// <summary>
/// Writes a skip the owner made by hand into the skip ledger the read-back keeps. It settles against <b>the ledger as it stands when it is
/// asked</b>, never against one handed over from earlier: the morning's read-back may have settled days since the tab was opened, and a
/// ledger carried from the tab would write over them. Settling the same skip twice leaves one record.
/// </summary>
public interface ISkipSettler
{
    /// <exception cref="InvalidOperationException">The ledger cannot be read, so nothing is written into it.</exception>
    /// <exception cref="IOException">The ledger could not be written.</exception>
    void Settle(SkipCommit commit);
}

/// <summary>
/// A plan source that settles every skip it is asked to record into the ledger (<see cref="ISkipSettler"/>) before the source underneath
/// records it. A skip that cannot be settled is not recorded at all, so the ledger and the source never disagree about what was skipped.
/// </summary>
public sealed class SettlingWorkoutsSource(IWorkoutsSource inner, ISkipSettler settler) : IWorkoutsSource
{
    private readonly IWorkoutsSource inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ISkipSettler settler = settler ?? throw new ArgumentNullException(nameof(settler));

    public WorkoutsRead Read(DateTimeOffset now) => inner.Read(now);

    public void Record(WorkoutsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change is WorkoutsChange.SkipCommitted skipped)
        {
            settler.Settle(new SkipCommit(skipped.Skip));
        }

        inner.Record(change);
    }
}
