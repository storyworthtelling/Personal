using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Settles a skip the owner made by hand into the skip ledger file, reading the ledger fresh and writing it back whole, so everything the
/// read-back has settled since the tab was opened is kept. A ledger that exists and cannot be read is never written over: it is refused with
/// a plain sentence, exactly as the read-back refuses.
/// </summary>
public sealed class LedgerSkipSettler(ILedgerStore store) : ISkipSettler
{
    private readonly ILedgerStore store = store ?? throw new ArgumentNullException(nameof(store));

    public void Settle(SkipCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var read = store.Load();
        if (read.WholeFileUnreadable)
        {
            throw new InvalidOperationException("The skip ledger could not be read, so a skip cannot be written into it; the ledger is left as it is.");
        }

        var next = commit.Record(read.Ledger);
        if (!ReferenceEquals(next, read.Ledger))
        {
            store.Save(next);
        }
    }
}
