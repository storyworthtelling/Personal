namespace FocusPhone.Domain.Timeline;

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
/// UndoBar, and the actual deletes.
/// </summary>
public sealed class DeleteBuffer
{
    private readonly Dictionary<(TimelineItemKind, string), PendingDelete> pending = [];

    public IReadOnlyCollection<PendingDelete> Pending => pending.Values;

    public PendingDelete Schedule(TimelineItem item, DateTimeOffset now, TimeSpan window)
    {
        var entry = new PendingDelete(item.Kind, item.SourceId, now + window);
        pending[(item.Kind, item.SourceId)] = entry;
        return entry;
    }

    public bool Undo(TimelineItemKind kind, string sourceId) => pending.Remove((kind, sourceId));

    public bool IsPending(TimelineItem item) => pending.ContainsKey((item.Kind, item.SourceId));

    /// <summary>Removes items the owner has deleted (still within their undo window) from a view.</summary>
    public IEnumerable<TimelineItem> Filter(IEnumerable<TimelineItem> items) =>
        items.Where(item => !IsPending(item));

    /// <summary>
    /// Deletes whose window has elapsed. They are removed from the buffer and
    /// returned so the caller can perform the irreversible channel deletion.
    /// </summary>
    public IReadOnlyList<PendingDelete> TakeDue(DateTimeOffset now)
    {
        var due = pending.Values.Where(entry => entry.CommitAtUtc <= now).ToList();
        foreach (var entry in due)
        {
            pending.Remove((entry.Kind, entry.SourceId));
        }
        return due;
    }
}
