using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

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

    [Fact]
    public void Merge_WithIdentityResolver_MergesAPersonsPhoneAndEmailIntoOneThread()
    {
        var phone = "5551234567";
        var email = "alice@example.com";
        var resolver = new IdentityMap([[phone, email]], [], []);

        var threads = new TimelineMerger().Merge(
            [
                Item(TimelineItemKind.Message, phone, 0, "m1"),
                Item(TimelineItemKind.Email, email, 10, "e1"),
            ],
            resolver);

        var thread = Assert.Single(threads);
        Assert.Equal(2, thread.Items.Count);
        Assert.Contains(phone, thread.MemberKeys);
        Assert.Contains(email, thread.MemberKeys);
        Assert.False(thread.IsGroup);
    }

    [Fact]
    public void Merge_GroupItems_ThreadByGroupIdRatherThanPerson()
    {
        var groupItemA = new TimelineItem(
            TimelineItemKind.Message, "5551110000", T0, "hi all", Incoming: true, SourceId: "g1")
        {
            GroupId = "conv-42",
            Participants = ["5551110000", "5552220000"],
        };
        var groupItemB = groupItemA with
        {
            ContactKey = "5552220000",
            TimestampUtc = T0.AddMinutes(5),
            SourceId = "g2",
        };

        var thread = Assert.Single(new TimelineMerger().Merge([groupItemA, groupItemB]));

        Assert.True(thread.IsGroup);
        Assert.Equal(2, thread.Items.Count);
        Assert.Equal(["5551110000", "5552220000"], thread.Participants);
    }

    [Fact]
    public void Merge_GroupItemsWithDifferingRawSpellings_UnionsOneAddressPerNormalizedKey()
    {
        // Two messages in the same group can each carry a differently-spelled raw address
        // for the same international contact (e.g. one dropped the leading "+"); the merged
        // thread must still resolve to exactly one address per participant, and it must keep
        // the country code rather than the normalized, undialable last-ten-digits form.
        var groupItemA = new TimelineItem(
            TimelineItemKind.Message, "5551110000", T0, "hi all", Incoming: true, SourceId: "g1")
        {
            GroupId = "conv-77",
            Participants = ["5551110000", "7911123456"],
            ParticipantAddresses = ["5551110000", "+447911123456"],
        };
        var groupItemB = groupItemA with
        {
            ContactKey = "7911123456",
            TimestampUtc = T0.AddMinutes(5),
            SourceId = "g2",
            ParticipantAddresses = ["5551110000", "447911123456"],
        };

        var thread = Assert.Single(new TimelineMerger().Merge([groupItemA, groupItemB]));

        Assert.Equal(2, thread.ParticipantAddresses.Count);
        Assert.Contains(thread.ParticipantAddresses, address => address.EndsWith("447911123456", StringComparison.Ordinal));
        Assert.DoesNotContain("7911123456", thread.ParticipantAddresses);
    }

    [Fact]
    public void Merge_MarksThreadUnread_WhenAnyItemIsUnread()
    {
        var read = new TimelineItem(TimelineItemKind.Message, "alice", T0, "read", Incoming: true, SourceId: "m1");
        var unread = new TimelineItem(
            TimelineItemKind.Message, "alice", T0.AddMinutes(1), "new", Incoming: true, SourceId: "m2")
        {
            Unread = true,
        };

        var thread = Assert.Single(new TimelineMerger().Merge([read, unread]));

        Assert.True(thread.HasUnread);
    }
}
