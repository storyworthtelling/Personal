using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class GroupSenderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static TimelineItem Item(string key, bool incoming, string? groupId = "mms:7") =>
        new(TimelineItemKind.Message, key, T0, "hi", incoming, "mms:1") { GroupId = groupId };

    private static string Name(string key) => key == "5550100" ? "Robin" : key;

    [Fact]
    public void AnIncomingMessageNamesItsSender() =>
        Assert.Equal("Robin", GroupSender.Label(Item("5550100", incoming: true), Name));

    [Fact]
    public void TheOwnersMessageSaysYou() =>
        Assert.Equal("You", GroupSender.Label(Item("5550100", incoming: false), Name));

    [Fact]
    public void AnItemKeyedOnTheGroupNamesNobody() =>
        Assert.Null(GroupSender.Label(Item("mms:7", incoming: true), Name));
}
