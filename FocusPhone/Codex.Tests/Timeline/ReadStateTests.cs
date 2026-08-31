using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class ReadStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static ContactThread Thread(string key, params TimelineItem[] items) =>
        new(key, items, items.Max(i => i.TimestampUtc));

    private static TimelineItem Incoming(int minute) =>
        new(TimelineItemKind.Message, "alice", T0.AddMinutes(minute), "hi", Incoming: true, SourceId: $"m{minute}");

    private static TimelineItem Outgoing(int minute) =>
        new(TimelineItemKind.Message, "alice", T0.AddMinutes(minute), "hi", Incoming: false, SourceId: $"o{minute}");

    [Fact]
    public void ThreadWithNoReadRecord_IsUnread_WhenItHasAnIncomingItem()
    {
        var state = new ReadState(new Dictionary<string, DateTimeOffset>());

        Assert.True(state.IsUnread(Thread("alice", Incoming(0))));
    }

    [Fact]
    public void ThreadReadAfterItsNewestItem_IsRead()
    {
        var state = new ReadState(new Dictionary<string, DateTimeOffset> { ["alice"] = T0.AddMinutes(10) });

        Assert.False(state.IsUnread(Thread("alice", Incoming(0), Incoming(5))));
    }

    [Fact]
    public void IncomingItemNewerThanLastRead_MakesThreadUnread()
    {
        var state = new ReadState(new Dictionary<string, DateTimeOffset> { ["alice"] = T0.AddMinutes(5) });

        Assert.True(state.IsUnread(Thread("alice", Incoming(0), Incoming(10))));
    }

    [Fact]
    public void OutgoingItems_DoNotMakeAThreadUnread()
    {
        var state = new ReadState(new Dictionary<string, DateTimeOffset> { ["alice"] = T0.AddMinutes(5) });

        Assert.False(state.IsUnread(Thread("alice", Incoming(0), Outgoing(10))));
    }

    [Fact]
    public void Apply_StampsHasUnreadOntoTheThread()
    {
        var state = new ReadState(new Dictionary<string, DateTimeOffset>());

        Assert.True(state.Apply(Thread("alice", Incoming(0))).HasUnread);
    }
}

public class InboxOrderingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static ContactThread Thread(string key, int minute, bool unread) =>
        new(key,
            [new TimelineItem(TimelineItemKind.Message, key, T0.AddMinutes(minute), "hi", true, $"{key}{minute}")],
            T0.AddMinutes(minute))
        {
            HasUnread = unread,
        };

    [Fact]
    public void UnreadThreadsComeFirst_ThenMostRecent()
    {
        var older = Thread("older", 0, unread: false);
        var newerRead = Thread("newerRead", 100, unread: false);
        var unread = Thread("unread", 50, unread: true);

        var ordered = InboxOrdering.UnreadFirstThenRecent([older, newerRead, unread]);

        Assert.Equal(["unread", "newerRead", "older"], ordered.Select(t => t.ContactKey).ToArray());
    }
}
