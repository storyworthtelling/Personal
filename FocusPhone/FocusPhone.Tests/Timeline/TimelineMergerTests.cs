using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class TimelineMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static TimelineItem Item(
        TimelineItemKind kind, string contactKey, int minutesAfterT0, string sourceId) =>
        new(kind, contactKey, T0.AddMinutes(minutesAfterT0),
            Snippet: $"snippet {sourceId}", Incoming: true, SourceId: sourceId);

    [Fact]
    public void Merge_GroupsItemsByContact()
    {
        var threads = new TimelineMerger().Merge([
            Item(TimelineItemKind.Call, "alice", 0, "c1"),
            Item(TimelineItemKind.Message, "bob", 5, "m1"),
            Item(TimelineItemKind.Email, "alice", 10, "e1"),
        ]);

        Assert.Equal(2, threads.Count);
        var alice = Assert.Single(threads, t => t.ContactKey == "alice");
        Assert.Equal(2, alice.Items.Count);
    }

    [Fact]
    public void Merge_OrdersThreadsByMostRecentActivityDescending()
    {
        var threads = new TimelineMerger().Merge([
            Item(TimelineItemKind.Message, "older", 0, "m1"),
            Item(TimelineItemKind.Message, "newer", 60, "m2"),
        ]);

        Assert.Equal(["newer", "older"], threads.Select(t => t.ContactKey).ToArray());
    }

    [Fact]
    public void Merge_OrdersItemsWithinThreadChronologically()
    {
        var threads = new TimelineMerger().Merge([
            Item(TimelineItemKind.Email, "alice", 30, "e1"),
            Item(TimelineItemKind.Call, "alice", 0, "c1"),
            Item(TimelineItemKind.Message, "alice", 15, "m1"),
        ]);

        Assert.Equal(["c1", "m1", "e1"], threads[0].Items.Select(i => i.SourceId).ToArray());
    }

    [Fact]
    public void Merge_DeduplicatesByKindAndSourceId()
    {
        var threads = new TimelineMerger().Merge([
            Item(TimelineItemKind.Message, "alice", 0, "m1"),
            Item(TimelineItemKind.Message, "alice", 0, "m1"),
            Item(TimelineItemKind.Call, "alice", 0, "m1"),
        ]);

        Assert.Equal(2, threads[0].Items.Count);
    }

    [Fact]
    public void Merge_SetsThreadLastActivityToNewestItem()
    {
        var threads = new TimelineMerger().Merge([
            Item(TimelineItemKind.Call, "alice", 0, "c1"),
            Item(TimelineItemKind.Email, "alice", 45, "e1"),
        ]);

        Assert.Equal(T0.AddMinutes(45), threads[0].LastActivityUtc);
    }

    [Fact]
    public void Merge_EmptyInput_ReturnsNoThreads()
    {
        Assert.Empty(new TimelineMerger().Merge([]));
    }

    [Fact]
    public void Merge_CollapsesProviderAndNotificationDuplicatesIntoOneItem()
    {
        var providerText = new TimelineItem(
            TimelineItemKind.Message, "5551234567", T0, "hey", Incoming: true, SourceId: "sms:12");
        var notificationText = new TimelineItem(
            TimelineItemKind.Message, "5551234567", T0.AddSeconds(30), "hey", Incoming: true,
            SourceId: "5551234567:1750000000000");

        var threads = new TimelineMerger().Merge([providerText, notificationText]);

        var thread = Assert.Single(threads);
        var item = Assert.Single(thread.Items);
        Assert.Equal("sms:12", item.SourceId);
    }
}
