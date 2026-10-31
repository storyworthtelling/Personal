using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The skip ledger in one file in the app's private storage, read and written whole. A file that exists and cannot be
/// read as a ledger is reported by the read (<see cref="SkipLedgerRead.WholeFileUnreadable"/>) and not touched; it is the
/// caller's job not to save over it (<see cref="ReadBackService"/> refuses, with <see cref="LedgerUnreadableException"/>).
/// The file holds days, kinds, outcomes and the person's first name: no mark, no box and no words from a page.
/// </summary>
public sealed class FileLedgerStore : ILedgerStore
{
    // One lock for every store in the process, as the other whole-file stores have: the worker and the screen share a file.
    private static readonly object FileLock = new();

    private readonly string path;

    public FileLedgerStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public SkipLedgerRead Load()
    {
        lock (FileLock)
        {
            return SkipLedger.FromJson(File.Exists(path) ? File.ReadAllText(path) : null);
        }
    }

    public void Save(SkipLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, ledger.ToJson());
        }
    }
}
