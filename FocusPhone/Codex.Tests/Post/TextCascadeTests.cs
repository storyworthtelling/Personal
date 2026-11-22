using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The phone's one send road (P14 B2), taken out of the Mailbox so the Mailbox and the wrist's relay send by the same
/// cascade: Google Messages over the bridge first, then a plain text to a person or one MMS to a group, and otherwise
/// nothing and the reason. Invented numbers throughout.
/// </summary>
public class TextCascadeTests
{
    private static readonly string[] Lee = ["+12125550101"];
    private static readonly string[] Group = ["+12125550101", "+12125550102", "+12125550103"];
    private static readonly MmsOutboundPart Photo = new("photo", "image/jpeg", [1, 2, 3]);

    // A phone whose routes are recorded rather than taken.
    private sealed class Routes : ITextRoutes
    {
        public string? Started { get; init; } = "conversation-new";
        public bool BridgeTakes { get; init; } = true;
        public Exception? TextFails { get; init; }
        public Exception? MmsFails { get; init; }
        public List<string> Calls { get; } = [];

        public string? FindOrStartConversation(IReadOnlyList<string> recipients)
        {
            Calls.Add($"start {recipients.Count}");
            return Started;
        }

        public bool TrySend(string conversationId, string text)
        {
            Calls.Add($"bridge {conversationId}");
            return BridgeTakes;
        }

        public bool TrySendMedia(string conversationId, MmsOutboundPart media)
        {
            Calls.Add($"bridge media {conversationId}");
            return BridgeTakes;
        }

        public void SendText(string number, string text)
        {
            Calls.Add($"text {number}");
            if (TextFails is not null)
            {
                throw TextFails;
            }
        }

        public void SendMms(IReadOnlyList<string> recipients, string? text, IReadOnlyList<MmsOutboundPart> media)
        {
            Calls.Add($"mms {recipients.Count} {(text is null ? "no words" : "words")} {media.Count}");
            if (MmsFails is not null)
            {
                throw MmsFails;
            }
        }
    }

    [Fact]
    public void TheBridgeLeads()
    {
        var routes = new Routes();

        var delivery = TextCascade.Text(routes, Lee, "conversation-7", "On my way.", bridgeConnected: true, canSendSms: true);

        Assert.Equal((TextSendPath.Bridge, true, true), (delivery.Path, delivery.BridgeTried, delivery.Went));
        Assert.Equal(["bridge conversation-7"], routes.Calls);
    }

    [Fact]
    public void ANewConversationStartsOnTheBridge()
    {
        var routes = new Routes();

        var delivery = TextCascade.Text(routes, Group, null, "See you all at six.", bridgeConnected: true, canSendSms: false);

        Assert.Equal(TextSendPath.Bridge, delivery.Path);
        Assert.Equal(["start 3", "bridge conversation-new"], routes.Calls);
    }

    [Fact]
    public void ARefusedBridgeFallsToAPlainText()
    {
        var routes = new Routes { BridgeTakes = false };

        var delivery = TextCascade.Text(routes, Lee, "conversation-7", "On my way.", bridgeConnected: true, canSendSms: true);

        Assert.Equal((TextSendPath.NativeSms, true, true), (delivery.Path, delivery.BridgeTried, delivery.Went));
        Assert.Equal(["bridge conversation-7", $"text {Lee[0]}"], routes.Calls);
    }

    [Fact]
    public void NoConversationFallsBack()
    {
        var routes = new Routes { Started = null };

        var delivery = TextCascade.Text(routes, Lee, null, "On my way.", bridgeConnected: true, canSendSms: true);

        Assert.Equal(TextSendPath.NativeSms, delivery.Path);
        Assert.Equal(["start 1", $"text {Lee[0]}"], routes.Calls);
    }

    [Fact]
    public void ARefusedBridgeToAGroupIsOneMms()
    {
        var routes = new Routes { BridgeTakes = false };

        var delivery = TextCascade.Text(routes, Group, "conversation-9", "Reply to all.", bridgeConnected: true, canSendSms: true);

        Assert.Equal(TextSendPath.GroupMms, delivery.Path);
        Assert.Equal(["bridge conversation-9", "mms 3 words 0"], routes.Calls);
    }

    [Fact]
    public void WithTheBridgeDownAPersonGetsAPlainText()
    {
        var routes = new Routes();

        var delivery = TextCascade.Text(routes, Lee, "conversation-7", "On my way.", bridgeConnected: false, canSendSms: true);

        Assert.Equal((TextSendPath.NativeSms, false), (delivery.Path, delivery.BridgeTried));
        Assert.Equal([$"text {Lee[0]}"], routes.Calls);
    }

    [Fact]
    public void WithTheBridgeDownAGroupIsOneMms()
    {
        var routes = new Routes();

        var delivery = TextCascade.Text(routes, Group, null, "Reply to all.", bridgeConnected: false, canSendSms: true);

        Assert.Equal(TextSendPath.GroupMms, delivery.Path);
        Assert.Equal(["mms 3 words 0"], routes.Calls);
    }

    [Fact]
    public void NothingCanCarryItSaysSo()
    {
        var routes = new Routes();

        var delivery = TextCascade.Text(routes, Lee, null, "On my way.", bridgeConnected: false, canSendSms: false);

        Assert.Equal((TextSendPath.Unavailable, false, false, null), (delivery.Path, delivery.BridgeTried, delivery.Went, delivery.Failure));
        Assert.Empty(routes.Calls);
    }

    [Fact]
    public void ARefusedBridgeWithNoTextIsUnavailableAfterTrying()
    {
        var routes = new Routes { BridgeTakes = false };

        var delivery = TextCascade.Text(routes, Lee, "conversation-7", "On my way.", bridgeConnected: true, canSendSms: false);

        Assert.Equal((TextSendPath.Unavailable, true, false), (delivery.Path, delivery.BridgeTried, delivery.Went));
        Assert.Equal(["bridge conversation-7"], routes.Calls);
    }

    [Fact]
    public void AFailedTextKeepsItsCause()
    {
        var cause = new InvalidOperationException("This device has no SMS manager.");
        var routes = new Routes { TextFails = cause };

        var delivery = TextCascade.Text(routes, Lee, null, "On my way.", bridgeConnected: false, canSendSms: true);

        Assert.Equal((TextSendPath.NativeSms, false), (delivery.Path, delivery.Went));
        Assert.Same(cause, delivery.Failure);
    }

    [Fact]
    public void AFailedMmsKeepsItsCause()
    {
        var cause = new MmsAttachmentTooLargeException(20_000_000, 15_000_000);
        var routes = new Routes { MmsFails = cause };

        var delivery = TextCascade.Text(routes, Group, null, "Reply to all.", bridgeConnected: false, canSendSms: true);

        Assert.Equal((TextSendPath.GroupMms, false), (delivery.Path, delivery.Went));
        Assert.Same(cause, delivery.Failure);
    }

    [Fact]
    public void APictureGoesByTheBridgeFirst()
    {
        var routes = new Routes();

        var delivery = TextCascade.Media(routes, Lee, null, Photo, bridgeConnected: true);

        Assert.Equal(TextSendPath.Bridge, delivery.Path);
        Assert.Equal(["start 1", "bridge media conversation-new"], routes.Calls);
    }

    [Fact]
    public void APictureWithoutTheBridgeIsAnMms()
    {
        // To one person as to a group: without the bridge a picture is an MMS, as the Mailbox always sent it.
        var routes = new Routes { BridgeTakes = false };

        var refused = TextCascade.Media(routes, Lee, "conversation-7", Photo, bridgeConnected: true);
        var down = TextCascade.Media(new Routes(), Lee, null, Photo, bridgeConnected: false);

        Assert.Equal((TextSendPath.GroupMms, true), (refused.Path, refused.BridgeTried));
        Assert.Equal(["bridge media conversation-7", "mms 1 no words 1"], routes.Calls);
        Assert.Equal((TextSendPath.GroupMms, false, true), (down.Path, down.BridgeTried, down.Went));
    }

    [Fact]
    public void AFailedPictureKeepsItsCause()
    {
        var cause = new MmsAttachmentTooLargeException(20_000_000, 15_000_000);

        var delivery = TextCascade.Media(new Routes { MmsFails = cause }, Group, null, Photo, bridgeConnected: false);

        Assert.Same(cause, delivery.Failure);
        Assert.False(delivery.Went);
    }
}
