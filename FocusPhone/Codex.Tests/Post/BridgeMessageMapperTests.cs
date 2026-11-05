using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A message from Google Messages (through the bridge) becomes a Mailbox item. A person's
/// texts thread on their number, so they meet the same person's calls and mail; a group threads
/// on Messages' own conversation id. Invented numbers throughout.
/// </summary>
public class BridgeMessageMapperTests
{
    private static readonly BridgeParticipant Me = new("1", "+15550100", "Me", true);
    private static readonly BridgeParticipant Robin = new("p2", "+1 (555) 555-0101", "Robin", false);
    private static readonly BridgeParticipant Lee = new("p3", "+15555550102", "Lee", false);

    private static BridgeConversation OneToOne() => new("c1", "Robin", false, [Me, Robin]);

    private static BridgeConversation Group() => new("g1", "Book club", true, [Me, Robin, Lee]);

    private static BridgeMessage Message(
        string conversationId, string participantId, bool fromMe, string text = "Hi",
        IReadOnlyList<BridgeMedia>? media = null) =>
        new("m1", conversationId, participantId, fromMe, text, 1_700_000_000_000_000, false, false, media ?? []);

    [Fact]
    public void AnIncomingTextThreadsOnTheSendersNumber()
    {
        var item = BridgeMessageMapper.Map(Message("c1", "p2", fromMe: false), OneToOne())!;

        Assert.Equal("5555550101", item.ContactKey);
        Assert.True(item.Incoming);
        Assert.Equal("gm:m1", item.SourceId);
        Assert.Equal("c1", item.ExternalConversationId);
        Assert.Equal("Hi", item.Snippet);
        Assert.Equal(TimelineItemKind.Message, item.Kind);
        Assert.Null(item.GroupId);
        Assert.Equal(BridgeTimestamp.ToInstant(1_700_000_000_000_000), item.TimestampUtc);
    }

    // The owner's own text in a one-to-one belongs to the thread of the person it went to.
    [Fact]
    public void AnOutgoingTextThreadsOnTheOtherPerson()
    {
        var item = BridgeMessageMapper.Map(Message("c1", "1", fromMe: true), OneToOne())!;

        Assert.Equal("5555550101", item.ContactKey);
        Assert.False(item.Incoming);
        Assert.False(item.Unread);
    }

    // History the bridge replays is not news; only a message that has just arrived is unread.
    [Fact]
    public void OnlyANewIncomingMessageIsUnread()
    {
        var fresh = BridgeMessageMapper.Map(Message("c1", "p2", fromMe: false), OneToOne())!;
        var old = BridgeMessageMapper.Map(Message("c1", "p2", fromMe: false) with { IsOld = true }, OneToOne())!;

        Assert.True(fresh.Unread);
        Assert.False(old.Unread);
    }

    [Fact]
    public void AGroupThreadsOnTheConversationWithEveryoneButTheOwner()
    {
        var item = BridgeMessageMapper.Map(Message("g1", "p3", fromMe: false), Group())!;

        Assert.Equal("gm:g1", item.GroupId);
        Assert.Equal("5555550102", item.ContactKey);
        Assert.Equal(["5555550101", "5555550102"], item.Participants);
        // The dialable spellings travel beside the keys, one per participant, in the same order.
        Assert.Equal(["+1 (555) 555-0101", "+15555550102"], item.ParticipantAddresses);
    }

    [Fact]
    public void TheOwnersTextInAGroupStillThreadsOnTheGroup()
    {
        var item = BridgeMessageMapper.Map(Message("g1", "1", fromMe: true), Group())!;

        Assert.Equal("gm:g1", item.GroupId);
        Assert.False(item.Incoming);
    }

    [Fact]
    public void APictureIsDescribedAndOfferedAsAnAttachment()
    {
        var media = new BridgeMedia("md1", "a2V5", "image/jpeg", "pic.jpg", 2048);

        var item = BridgeMessageMapper.Map(Message("c1", "p2", false, text: "", media: [media]), OneToOne())!;

        Assert.Equal("Photo", item.Snippet);
        var attachment = Assert.Single(item.Attachments);
        Assert.Equal(BridgeMessageMapper.AttachmentId(media), attachment.Id);
        Assert.Equal("image/jpeg", attachment.MimeType);
        Assert.Equal(2048, attachment.SizeBytes);
    }

    [Fact]
    public void AnAttachmentIdCarriesWhatTheDownloadNeeds()
    {
        var media = new BridgeMedia("md1", "a2V5", "image/jpeg", "pic.jpg", 2048);

        Assert.Equal(("md1", "a2V5"), BridgeMessageMapper.ReadAttachmentId(BridgeMessageMapper.AttachmentId(media)));
        Assert.Null(BridgeMessageMapper.ReadAttachmentId("mms-part:4"));
    }

    [Fact]
    public void ATextWithAPictureKeepsItsWords()
    {
        var media = new BridgeMedia("md1", "a2V5", "image/jpeg", "pic.jpg", 2048);

        var item = BridgeMessageMapper.Map(Message("c1", "p2", false, text: "Look", media: [media]), OneToOne())!;

        Assert.Equal("Look", item.Snippet);
    }

    // A message with neither words nor a picture is a status row Messages keeps for itself.
    [Fact]
    public void AnEmptyMessageIsNotAnItem()
    {
        Assert.Null(BridgeMessageMapper.Map(Message("c1", "p2", false, text: ""), OneToOne()));
    }

    // Before the conversation is known (an event can beat its conversation), the sender's
    // own participant id is the only key there is; it threads alone until the next sync.
    [Fact]
    public void AMessageFromAnUnknownConversationStillLands()
    {
        var item = BridgeMessageMapper.Map(Message("c9", "p7", fromMe: false), conversation: null)!;

        Assert.Equal("gm:c9", item.ContactKey);
        Assert.Equal("c9", item.ExternalConversationId);
    }

    [Fact]
    public void AFailedSendSaysSo()
    {
        var item = BridgeMessageMapper.Map(Message("c1", "1", fromMe: true) with { Failed = true }, OneToOne())!;

        Assert.Equal("Not sent: Hi", item.Snippet);
    }
}
