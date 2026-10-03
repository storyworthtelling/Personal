using Index.Domain.Model;

namespace Index.Infrastructure.Services;

/// <summary>
/// A bank that has just been disconnected and is still inside its undo window: the whole institution row
/// and every account that was cascade-deleted with it, captured BEFORE the delete with their original ids
/// and every user-set field. The undo re-inserts exactly this (<see cref="AccountService.UndoDisconnectAsync"/>);
/// letting the window close releases the Item at Plaid (<see cref="AccountService.CommitDisconnectAsync"/>).
///
/// <para>It settles exactly once: whichever of undo and commit gets there first wins, so a tap on the
/// dog-ear and the window's timer firing together can neither release an Item that was just restored nor
/// restore one whose Plaid token has just been killed.</para>
/// </summary>
public sealed class DisconnectedInstitution
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Outcome outcome = Outcome.Pending;

    internal DisconnectedInstitution(Institution snapshot)
    {
        Snapshot = snapshot;
    }

    /// <summary>The detached institution, its <see cref="Institution.Accounts"/> included.</summary>
    public Institution Snapshot { get; }

    public Guid Id => Snapshot.Id;

    public string Name => Snapshot.Name;

    /// <summary>False for a hand-entered bank: it has no Plaid Item, so there is nothing to release.</summary>
    public bool HasPlaidItem => !string.IsNullOrWhiteSpace(Snapshot.AccessToken);

    internal async Task<bool> TrySettleAsync(Outcome wanted, Func<Task<bool>> work)
    {
        await gate.WaitAsync();
        try
        {
            if (outcome != Outcome.Pending)
                return false;
            // The outcome is recorded only when the work really happened, so an undo that failed (the
            // database was busy) can be tried again instead of being spent.
            if (!await work())
                return false;
            outcome = wanted;
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    internal enum Outcome
    {
        Pending,
        Undone,
        Released,
    }
}
