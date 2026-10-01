namespace Codex.Domain.Timeline;

/// <summary>
/// A delete the owner asked for that has not yet been committed. The item
/// disappears from view immediately, but the irreversible channel action (Gmail
/// trash, call-log row removal, bridge remote-delete) is held until CommitAtUtc
/// so the undo bar can still call it back.
/// </summary>
public record PendingDelete(TimelineItemKind Kind, string SourceId, DateTimeOffset CommitAtUtc);

/// <summary>
/// The undo-first delete state machine (DESIGN.md §7): scheduling a delete hides
/// the item at once and starts a timer; undo cancels it before the timer fires;
/// once due, the item is handed back to the caller to perform the real, channel-
/// specific deletion. Pure and testable — the Android layer owns the clock, the
/// dog-ear, and the actual deletes.
///
/// One instance is shared between the UI thread (Schedule/Undo, driven by a
/// button tap and the undo timer's Handler.PostDelayed) and a background thread
/// (Filter, called from MailboxArea's Task.Run reload/merge path) — so every
/// access to the backing dictionary is locked. An unsynchronized Dictionary
/// read racing a concurrent write can corrupt its internal buckets and throw
/// from inside a bare Task.Run, which is unhandled and can crash the process;
/// on the SMS role holder, a crash-looping app gets force-stopped by Android,
/// which silently stops manifest-declared receivers (including the one that
/// persists incoming texts) until the owner relaunches it by hand.
/// </summary>
public sealed class DeleteBuffer
{
    private readonly object gate = new();
    private readonly Dictionary<(TimelineItemKind, string), PendingDelete> pending = [];

    public IReadOnlyCollection<PendingDelete> Pending
    {
        get { lock (gate) { return pending.Values.ToList(); } }
    }

    public PendingDelete Schedule(TimelineItem item, DateTimeOffset now, TimeSpan window)
    {
        var entry = new PendingDelete(item.Kind, item.SourceId, now + window);
        lock (gate)
        {
            pending[(item.Kind, item.SourceId)] = entry;
        }
        return entry;
    }

    public bool Undo(TimelineItemKind kind, string sourceId)
    {
        lock (gate)
        {
            return pending.Remove((kind, sourceId));
        }
    }

    public bool IsPending(TimelineItem item)
    {
        lock (gate)
        {
            return pending.ContainsKey((item.Kind, item.SourceId));
        }
    }

    /// <summary>Removes items the owner has deleted (still within their undo window) from a view.</summary>
    public IEnumerable<TimelineItem> Filter(IEnumerable<TimelineItem> items)
    {
        // Snapshot the pending set under the lock, then filter against the snapshot: this
        // avoids holding the lock (and calling into the caller's IEnumerable) for the
        // duration of the enumeration, which could otherwise deadlock or hold the UI
        // thread's Schedule/Undo behind a slow background merge.
        HashSet<(TimelineItemKind, string)> snapshot;
        lock (gate)
        {
            snapshot = [.. pending.Keys];
        }
        return items.Where(item => !snapshot.Contains((item.Kind, item.SourceId)));
    }

    /// <summary>
    /// Deletes whose window has elapsed. They are removed from the buffer and
    /// returned so the caller can perform the irreversible channel deletion.
    /// </summary>
    public IReadOnlyList<PendingDelete> TakeDue(DateTimeOffset now)
    {
        lock (gate)
        {
            var due = pending.Values.Where(entry => entry.CommitAtUtc <= now).ToList();
            foreach (var entry in due)
            {
                pending.Remove((entry.Kind, entry.SourceId));
            }
            return due;
        }
    }
}
