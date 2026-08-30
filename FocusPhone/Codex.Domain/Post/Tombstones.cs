namespace Codex.Domain.Post;

/// <summary>One filing that the source has not yet confirmed.</summary>
public sealed record Tombstone(string SourceId, DateTimeOffset FiledAtUtc, int FailedAttempts);

/// <summary>
/// The load-bearing invariant of the phase.
///
/// Filing means <i>delete at the source, keep a ghost here</i>. That directly contradicts
/// how reconcile works — it mirrors the source, so anything the source still has comes
/// back — and the two cannot both be naive. Getting it wrong does not look like a bug: a
/// filed message simply reappears in the Mailbox, again and again, and the owner concludes
/// that filing does not work.
///
/// The resolution, in four steps:
/// <list type="number">
/// <item>Filing writes a tombstone and hides the item at once. Waiting for the source to
/// confirm would make filing feel broken on a slow network, which on a phone is most of
/// the time.</item>
/// <item>The source delete is attempted, and retried while the tombstone stands.</item>
/// <item>Reconcile mirrors the source EXCEPT where a tombstone says the owner filed it, so
/// a not-yet-deleted message is never resurrected.</item>
/// <item>When the source confirms, the tombstone is dropped: the item is genuinely gone
/// there, reconcile cannot bring it back, and the ghost lives on in the archive.</item>
/// </list>
///
/// So a tombstone is <b>temporary</b>, and exists exactly as long as Post and the source
/// disagree about whether something is gone.
///
/// A delete that keeps failing eventually raises a dog-ear, but the item stays filed
/// regardless: the owner's intent stands whatever the server does. The retry budget
/// decides when to stop being quiet, never whether to give the message back.
/// </summary>
public sealed class Tombstones
{
    /// <summary>
    /// How many failures pass before the fold speaks. A phone loses its network constantly
    /// and a fold for every blip is a fold nobody reads.
    /// </summary>
    public const int QuietAttempts = 3;

    private readonly Dictionary<string, Tombstone> standing = [];

    /// <summary>Files an item: hidden here from this moment, whatever the source says.</summary>
    public void Mark(string sourceId, DateTimeOffset now)
    {
        // Filing something already filed must not reset a retry count that is close to
        // giving up, so an existing tombstone is left exactly as it stands.
        if (!standing.ContainsKey(sourceId))
        {
            standing[sourceId] = new Tombstone(sourceId, now, 0);
        }
    }

    /// <summary>Whether reconcile must keep suppressing this item.</summary>
    public bool IsFiled(string sourceId) => standing.ContainsKey(sourceId);

    /// <summary>The source confirmed the delete; the disagreement is over.</summary>
    public void SourceConfirmed(string sourceId) => standing.Remove(sourceId);

    /// <summary>Records a failed source delete. The item stays filed.</summary>
    public void AttemptFailed(string sourceId, DateTimeOffset now)
    {
        if (standing.TryGetValue(sourceId, out var tombstone))
        {
            standing[sourceId] = tombstone with { FailedAttempts = tombstone.FailedAttempts + 1 };
            return;
        }
        standing[sourceId] = new Tombstone(sourceId, now, 1);
    }

    public int FailedAttempts(string sourceId) =>
        standing.TryGetValue(sourceId, out var tombstone) ? tombstone.FailedAttempts : 0;

    /// <summary>Whether this filing has failed often enough to be worth a fold.</summary>
    public bool ShouldSpeak(string sourceId) => FailedAttempts(sourceId) >= QuietAttempts;

    /// <summary>
    /// Deletes still owed to the source, retried on every sync until one lands. There is
    /// deliberately no backoff: the retry is a handful of ids against a request the sync
    /// is making anyway, and a filing the source has not accepted is exactly the thing
    /// that should keep being pushed.
    /// </summary>
    public IReadOnlyList<string> Outstanding() =>
        standing.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// What the source offers, minus anything the owner has filed. This is the one line
    /// that stops a filed message walking back into the Mailbox.
    /// </summary>
    public IReadOnlyList<string> Reconcile(IEnumerable<string> sourceIds) =>
        sourceIds.Where(id => !IsFiled(id)).ToList();

    /// <summary>Every standing tombstone, for persisting between launches.</summary>
    public IReadOnlyCollection<Tombstone> All => standing.Values;

    public static Tombstones From(IEnumerable<Tombstone> tombstones)
    {
        var book = new Tombstones();
        foreach (var tombstone in tombstones)
        {
            book.standing[tombstone.SourceId] = tombstone;
        }
        return book;
    }
}
