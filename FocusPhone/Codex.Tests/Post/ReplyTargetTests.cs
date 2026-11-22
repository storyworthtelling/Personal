using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>Where a text in a correspondence goes, for the Mailbox and the watch alike (P14 B1).</summary>
public class ReplyTargetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, TimeSpan.Zero);

    private static TimelineItem Text(string who, int minute, string? conversation = null, string? group = null) =>
        new(TimelineItemKind.Message, who, T0.AddMinutes(minute), "hi", Incoming: true, SourceId: $"sms:{minute}")
        {
            ExternalConversationId = conversation,
            GroupId = group,
        };

    [Fact]
    public void APersonIsTextedOnTheirFirstNumber()
    {
        Assert.Equal("2125550101", ReplyTarget.PhoneOf(["2125550101", "lee@example.com"], "2125550101"));
        Assert.Equal("2125550101", ReplyTarget.PhoneOf(["lee@example.com", "2125550101"], "lee@example.com"));
        Assert.Equal("2125550101", ReplyTarget.PhoneOf([], "2125550101"));
        Assert.Null(ReplyTarget.PhoneOf(["lee@example.com"], "lee@example.com"));
    }

    [Theory]
    [InlineData("2125550101", true)]
    [InlineData("+12125550101", false)]
    [InlineData("lee@example.com", false)]
    [InlineData("", false)]
    public void APhoneKeyIsDigits(string key, bool phone)
    {
        Assert.Equal(phone, ReplyTarget.IsPhone(key));
    }

    [Fact]
    public void AGroupGoesToItsMembersOwnNumbers()
    {
        Assert.Equal(["+1 212-555-0101", "+44 7700 900123"],
            ReplyTarget.GroupRecipients(["+1 212-555-0101", "+44 7700 900123"], ["2125550101", "7700900123"]));
        Assert.Equal(["2125550101"], ReplyTarget.GroupRecipients([], ["2125550101"]));
    }

    [Fact]
    public void AGroupKnownOnlyFromNotificationsIsNotKnown()
    {
        Assert.True(ReplyTarget.MembershipKnown([Text("2125550101", 0, group: "mms:17"), Text("2125550102", 1, group: "mms:17")]));
        Assert.False(ReplyTarget.MembershipKnown([Text("2125550101", 0, group: MessagingNoticeMapper.GroupPrefix + "team")]));
        Assert.True(ReplyTarget.MembershipKnown([Text("2125550101", 0)]));
    }

    [Fact]
    public void TheBridgeRepliesIntoTheLatestConversation()
    {
        Assert.Equal("conv-2", ReplyTarget.ConversationOf([Text("2125550101", 0, "conv-1"), Text("2125550101", 1, "conv-2"), Text("2125550101", 2)]));
        Assert.Null(ReplyTarget.ConversationOf([Text("2125550101", 0)]));
    }

    [Fact]
    public void APersonsTarget()
    {
        var thread = new TimelineMerger().Merge([Text("2125550101", 0, "conv-lee"), Text("2125550101", 1)]).Single();

        var target = ReplyTarget.For(thread);

        Assert.NotNull(target);
        Assert.Equal(["2125550101"], target.Recipients);
        Assert.Equal("conv-lee", target.ConversationId);
        Assert.False(target.ToGroup);
    }

    [Fact]
    public void AGroupsTarget()
    {
        var items = new[] { Text("2125550101", 0, group: "mms:17"), Text("2125550102", 1, group: "mms:17") }
            .Select(item => item with { Participants = ["2125550101", "2125550102"], ParticipantAddresses = ["+12125550101", "+12125550102"] });
        var thread = new TimelineMerger().Merge(items).Single();

        var target = ReplyTarget.For(thread);

        Assert.NotNull(target);
        Assert.Equal(["+12125550101", "+12125550102"], target.Recipients);
        Assert.True(target.ToGroup);
    }

    [Fact]
    public void NowhereToGoIsNoTarget()
    {
        var email = new TimelineMerger().Merge([new TimelineItem(TimelineItemKind.Email, "lee@example.com", T0, "hi", true, "mail:1")]).Single();
        var heard = new TimelineMerger().Merge([Text("2125550101", 0, group: MessagingNoticeMapper.GroupPrefix + "team") with
        {
            Participants = ["2125550101"],
        }]).Single();
        var empty = new TimelineMerger().Merge([Text("2125550101", 0, group: "mms:17")]).Single();

        Assert.Null(ReplyTarget.For(email));
        Assert.Null(ReplyTarget.For(heard));
        Assert.Null(ReplyTarget.For(empty));
    }
}
