namespace FocusPhone.Domain.Timeline;

/// <summary>
/// Tracks which threads have been read, keyed by a per-thread last-read
/// timestamp. A thread is unread when it holds an incoming item newer than the
/// last time the owner opened it. Marking a thread read records "now". The
/// Android layer persists the timestamps; this stays pure so the unread rule is
/// testable.
/// </summary>
public sealed class ReadState
{
    private readonly IReadOnlyDictionary<string, DateTimeOffset> lastReadByThread;

    public ReadState(IReadOnlyDictionary<string, DateTimeOffset> lastReadByThread)
    {
        this.lastReadByThread = lastReadByThread;
    }

    public bool IsUnread(ContactThread thread)
    {
        var lastRead = LastRead(thread.ContactKey);
        return thread.Items.Any(item => item.Incoming && item.TimestampUtc > lastRead);
    }

    /// <summary>Stamps the thread's HasUnread from the persisted read timestamps.</summary>
    public ContactThread Apply(ContactThread thread) => thread with { HasUnread = IsUnread(thread) };

    private DateTimeOffset LastRead(string threadKey) =>
        lastReadByThread.TryGetValue(threadKey, out var timestamp) ? timestamp : DateTimeOffset.MinValue;
}

/// <summary>
/// Inbox ordering: unread threads first, then by most-recent activity — the
/// owner's chosen "check what's new" triage order.
/// </summary>
public static class InboxOrdering
{
    public static IReadOnlyList<ContactThread> UnreadFirstThenRecent(IEnumerable<ContactThread> threads) =>
        threads
            .OrderByDescending(thread => thread.HasUnread)
            .ThenByDescending(thread => thread.LastActivityUtc)
            .ToList();
}
