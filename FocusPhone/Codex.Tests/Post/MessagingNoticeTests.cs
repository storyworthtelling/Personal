using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

// What a Google Messages notification becomes in the timeline. This is the only route an
// RCS message has into Post while the bridge is unpaired, so a group RCS message keyed on
// its sender alone files a group conversation under one member's name.
public class MessagingNoticeTests
{
    private const long T0 = 1_780_000_000_000;

    // Stands in for the head's name → number lookup; anything unknown keys on itself.
    private static string KeyFor(string name) => name switch
    {
        "Avery Example" => "5550100001",
        "Blake Example" => "5550100002",
        _ => name.ToLowerInvariant(),
    };

    private static MessagingNotice Group(params NoticeMessage[] messages) =>
        new("conv-42", "Book club", IsGroup: true, messages);

    [Fact]
    public void GroupMessages_ThreadOnTheConversationNotTheSender()
    {
        var items = MessagingNoticeMapper.Map(
            Group(
                new NoticeMessage("Avery Example", T0, "Thursday?"),
                new NoticeMessage("Blake Example", T0 + 1000, "Works for me")),
            KeyFor);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("rcs:conv-42", item.GroupId));
        Assert.Equal("5550100001", items[0].ContactKey);
        Assert.Equal("5550100002", items[1].ContactKey);
    }

    [Fact]
    public void GroupMessages_CarryEverySenderSeenAsParticipants()
    {
        var items = MessagingNoticeMapper.Map(
            Group(
                new NoticeMessage("Avery Example", T0, "a"),
                new NoticeMessage("Blake Example", T0 + 1000, "b"),
                new NoticeMessage("Avery Example", T0 + 2000, "c")),
            KeyFor);

        Assert.All(items, item => Assert.Equal(["5550100001", "5550100002"], item.Participants));
    }

    // A notification only names the people who have spoken, so the list is not the group.
    // Leaving the dialable addresses empty is what stops a reply going out as an MMS to
    // part of the group.
    [Fact]
    public void GroupMessages_NeverClaimDialableAddresses()
    {
        var items = MessagingNoticeMapper.Map(Group(new NoticeMessage("Avery Example", T0, "a")), KeyFor);

        Assert.Empty(Assert.Single(items).ParticipantAddresses);
        Assert.False(GroupMembership.IsKnown(items[0].GroupId));
    }

    [Fact]
    public void ProviderGroups_HaveKnownMembership()
    {
        Assert.True(GroupMembership.IsKnown("mms:17"));
        Assert.False(GroupMembership.IsKnown(null));
    }

    [Fact]
    public void OneToOneMessages_KeyOnTheSenderAsBefore()
    {
        var notice = new MessagingNotice("conv-7", "Avery Example", IsGroup: false,
            [new NoticeMessage("Avery Example", T0, "hi")]);

        var item = Assert.Single(MessagingNoticeMapper.Map(notice, KeyFor));

        Assert.Null(item.GroupId);
        Assert.Equal("5550100001", item.ContactKey);
        // Unchanged, so a message already cached under this id is updated rather than doubled.
        Assert.Equal($"5550100001:{T0}", item.SourceId);
        Assert.True(item.Incoming);
    }

    // MessagingStyle gives the owner's own replies no sender. They used to be read as
    // incoming from the conversation title — the other person, in a one-to-one.
    [Fact]
    public void MessagesWithNoSender_AreTheOwnersOwn()
    {
        var notice = new MessagingNotice("conv-7", "Avery Example", IsGroup: false,
        [
            new NoticeMessage("Avery Example", T0, "hi"),
            new NoticeMessage(null, T0 + 1000, "hello back"),
        ]);

        var items = MessagingNoticeMapper.Map(notice, KeyFor);

        Assert.True(items[0].Incoming);
        Assert.False(items[1].Incoming);
        Assert.Equal("5550100001", items[1].ContactKey);
    }

    [Fact]
    public void OwnersMessageInAGroup_ThreadsWithTheGroup()
    {
        var items = MessagingNoticeMapper.Map(
            Group(new NoticeMessage("Avery Example", T0, "a"), new NoticeMessage(null, T0 + 1000, "me")),
            KeyFor);

        Assert.False(items[1].Incoming);
        Assert.Equal("rcs:conv-42", items[1].GroupId);
        Assert.Equal(["5550100001"], items[1].Participants);
    }

    [Fact]
    public void GroupWithoutAConversationId_KeysOnItsTitle()
    {
        var notice = new MessagingNotice(null, "Book club", IsGroup: true,
            [new NoticeMessage("Avery Example", T0, "a")]);

        Assert.Equal("rcs:title:Book club", Assert.Single(MessagingNoticeMapper.Map(notice, KeyFor)).GroupId);
    }

    // With neither there is nothing stable to thread a group on, so it falls back to the
    // sender rather than inventing a key that would split the group per notification.
    [Fact]
    public void GroupWithNothingToKeyOn_FallsBackToTheSender()
    {
        var notice = new MessagingNotice(null, null, IsGroup: true,
            [new NoticeMessage("Avery Example", T0, "a")]);

        Assert.Null(Assert.Single(MessagingNoticeMapper.Map(notice, KeyFor)).GroupId);
    }

    [Fact]
    public void OneToOneOwnMessageWithNoTitle_IsDropped()
    {
        var notice = new MessagingNotice("conv-7", null, IsGroup: false,
            [new NoticeMessage(null, T0, "who is this to?")]);

        Assert.Empty(MessagingNoticeMapper.Map(notice, KeyFor));
    }

    [Fact]
    public void GroupTitle_IsOfferedOnlyForAGroup()
    {
        Assert.Equal("Book club", MessagingNoticeMapper.GroupTitle(Group()));
        Assert.Null(MessagingNoticeMapper.GroupTitle(
            new MessagingNotice("conv-7", "Avery Example", IsGroup: false, [])));
    }
}
