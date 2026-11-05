using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The bridge (gmbridge/bridge.go) speaks JSON documents; these tests are the C# half of that
/// contract. Every shape here is written the way bridge.go writes it.
/// </summary>
public class BridgeEventsTests
{
    [Fact]
    public void AMessageReadsWithItsMedia()
    {
        var json = """
            {"type":"message","messageId":"m1","conversationId":"c1","participantId":"p2","fromMe":false,
             "status":"INCOMING_COMPLETE","failed":false,"text":"Hello","timestamp":1700000000000000,
             "tmpId":"","isOld":true,
             "media":[{"mediaId":"md1","key":"a2V5","mime":"image/jpeg","name":"pic.jpg","size":2048}]}
            """;

        var message = Assert.IsType<BridgeMessage>(BridgeEvents.Read(json));

        Assert.Equal("m1", message.MessageId);
        Assert.Equal("c1", message.ConversationId);
        Assert.Equal("p2", message.ParticipantId);
        Assert.False(message.FromMe);
        Assert.Equal("Hello", message.Text);
        Assert.Equal(1700000000000000, message.Timestamp);
        Assert.True(message.IsOld);
        var media = Assert.Single(message.Media);
        Assert.Equal(new BridgeMedia("md1", "a2V5", "image/jpeg", "pic.jpg", 2048), media);
    }

    [Fact]
    public void AConversationReadsWithItsParticipants()
    {
        var json = """
            {"type":"conversation","conversationId":"c1","name":"Book club","isGroup":true,"isRcs":true,
             "unread":true,"timestamp":5,
             "participants":[
               {"id":"1","number":"+15550100","formattedNumber":"(555) 0100","name":"Me","isMe":true},
               {"id":"p2","number":"+15550101","formattedNumber":"(555) 0101","name":"Robin","isMe":false}]}
            """;

        var conversation = Assert.IsType<BridgeConversation>(BridgeEvents.Read(json));

        Assert.Equal("Book club", conversation.Name);
        Assert.True(conversation.IsGroup);
        Assert.Equal(2, conversation.Participants.Count);
        Assert.Equal(new BridgeParticipant("p2", "+15550101", "Robin", false), conversation.Participants[1]);
        Assert.Equal(5, conversation.LastTimestamp);
    }

    // A sync reads only what moved since the last one, and remembers how far it got.
    [Fact]
    public void ASyncReadsOnlyConversationsThatMoved()
    {
        BridgeConversation At(string id, long stamp) => new(id, id, false, []) { LastTimestamp = stamp };

        var plan = BridgeSync.Moved([At("a", 10), At("b", 30), At("c", 20)], syncedThrough: 15);

        Assert.Equal(["b", "c"], plan.Conversations.Select(c => c.ConversationId));
        Assert.Equal(30, plan.Through);
    }

    [Fact]
    public void ASyncWithNothingNewKeepsItsPlace()
    {
        var plan = BridgeSync.Moved([new BridgeConversation("a", "a", false, []) { LastTimestamp = 3 }], syncedThrough: 9);

        Assert.Empty(plan.Conversations);
        Assert.Equal(9, plan.Through);
    }

    [Fact]
    public void PairingOutcomesRead()
    {
        Assert.Equal(new BridgePaired("{\"x\":1}"), BridgeEvents.Read("""{"type":"paired","session":"{\"x\":1}"}"""));
        Assert.Equal(new BridgePairFailed(PairingFailure.WrongEmoji, "picked the wrong one"),
            BridgeEvents.Read("""{"type":"pairFailed","reason":"wrongEmoji","error":"picked the wrong one"}"""));
    }

    [Theory]
    [InlineData("wrongEmoji", PairingFailure.WrongEmoji)]
    [InlineData("cancelled", PairingFailure.Cancelled)]
    [InlineData("timeout", PairingFailure.TimedOut)]
    [InlineData("noDevices", PairingFailure.NoPhone)]
    [InlineData("phoneNotResponding", PairingFailure.PhoneNotResponding)]
    [InlineData("signedOut", PairingFailure.SignedOut)]
    [InlineData("other", PairingFailure.Other)]
    [InlineData("something new", PairingFailure.Other)]
    public void EveryPairingReasonHasAName(string reason, PairingFailure expected)
    {
        var failed = Assert.IsType<BridgePairFailed>(
            BridgeEvents.Read($$"""{"type":"pairFailed","reason":"{{reason}}","error":"e"}"""));
        Assert.Equal(expected, failed.Reason);
    }

    [Fact]
    public void SessionAndConnectionEventsRead()
    {
        Assert.Equal(new BridgeSessionChanged("s"), BridgeEvents.Read("""{"type":"sessionChanged","session":"s"}"""));
        Assert.Equal(new BridgeLoggedOut("gone"), BridgeEvents.Read("""{"type":"loggedOut","reason":"gone"}"""));
        Assert.Equal(new BridgeConnection(BridgeConnectionState.Inactive, null),
            BridgeEvents.Read("""{"type":"connection","state":"inactive"}"""));
        Assert.Equal(new BridgeConnection(BridgeConnectionState.ListenError, "boom"),
            BridgeEvents.Read("""{"type":"connection","state":"listenError","error":"boom"}"""));
    }

    // Anything the reader does not know is not a crash: a newer bridge can add an event, and
    // a garbled one is skipped.
    [Theory]
    [InlineData("""{"type":"log","message":"*events.Something"}""")]
    [InlineData("""{"type":"brandNew"}""")]
    [InlineData("""{"no":"type"}""")]
    [InlineData("not json")]
    [InlineData("""{"type":"message"}""")]
    [InlineData("""{"type":"paired"}""")]
    public void UnknownOrIncompleteEventsReadAsNothing(string json)
    {
        Assert.Null(BridgeEvents.Read(json));
    }

    [Fact]
    public void AnUnknownConnectionStateIsKeptAsUnknown()
    {
        Assert.Equal(new BridgeConnection(BridgeConnectionState.Unknown, null),
            BridgeEvents.Read("""{"type":"connection","state":"later"}"""));
    }

    [Fact]
    public void AConversationListReads()
    {
        var list = BridgeEvents.ReadConversations("""
            [{"type":"conversation","conversationId":"c1","name":"A","isGroup":false,"participants":[]},
             {"type":"conversation","conversationId":"","name":"no id"}]
            """);

        Assert.Equal("c1", Assert.Single(list).ConversationId);
    }

    [Fact]
    public void AMessageListReads()
    {
        var list = BridgeEvents.ReadMessages("""
            [{"type":"message","messageId":"m1","conversationId":"c1","participantId":"p","fromMe":true,"text":"a","timestamp":1}]
            """);

        Assert.True(Assert.Single(list).FromMe);
    }

    [Fact]
    public void AGarbledListReadsAsEmpty()
    {
        Assert.Empty(BridgeEvents.ReadMessages("{"));
        Assert.Empty(BridgeEvents.ReadConversations("null"));
    }
}
