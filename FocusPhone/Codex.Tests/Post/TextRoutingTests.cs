using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Sending a text and deleting one for real are the two halves of the live pain, and both
/// have several possible routes with a strict order of preference. Which route is taken is
/// decided here so the screen only carries it out — and so "it silently did nothing" is a
/// testable state rather than a thing discovered on a phone.
/// </summary>
public class TextRoutingTests
{
    // ---- deleting ----

    // Holding the SMS role is what makes a real delete possible at all: the provider
    // accepts writes only from the default app. This is the case the whole role exists for.
    [Theory]
    [InlineData("sms:41", TextDeletePath.SmsProvider)]
    [InlineData("mms:7", TextDeletePath.MmsProvider)]
    public void WithTheRoleAProviderRowIsDeletedAtTheSource(string sourceId, TextDeletePath expected)
    {
        Assert.Equal(expected, TextDelete.Choose(sourceId, holdsSmsRole: true, bridgeConnected: false));
    }

    // Without the role Post cannot touch the provider, so the most it can do is stop
    // showing the message. That is a weaker outcome and the caller has to know it, which
    // is why it is a distinct answer rather than a silent no-op.
    [Theory]
    [InlineData("sms:41")]
    [InlineData("mms:7")]
    public void WithoutTheRoleAProviderRowCanOnlyBeHidden(string sourceId)
    {
        Assert.Equal(TextDeletePath.HideLocally,
            TextDelete.Choose(sourceId, holdsSmsRole: false, bridgeConnected: true));
    }

    // A bridge message never reached the provider — RCS does not — so the bridge is the
    // only thing that can delete it, and only while it is connected.
    [Fact]
    public void ABridgeMessageGoesBackThroughTheBridge()
    {
        Assert.Equal(TextDeletePath.Bridge,
            TextDelete.Choose("gm:abc", holdsSmsRole: true, bridgeConnected: true));
    }

    [Fact]
    public void ABridgeMessageWithNoBridgeCanOnlyBeHidden()
    {
        Assert.Equal(TextDeletePath.HideLocally,
            TextDelete.Choose("gm:abc", holdsSmsRole: true, bridgeConnected: false));
    }

    // An id from neither source is not a crash: hiding it is always available and always
    // does what the owner asked for on this screen.
    [Theory]
    [InlineData("")]
    [InlineData("something-else")]
    [InlineData("sms")]
    public void AnUnrecognizedIdIsHidden(string sourceId)
    {
        Assert.Equal(TextDeletePath.HideLocally,
            TextDelete.Choose(sourceId, holdsSmsRole: true, bridgeConnected: true));
    }

    // ---- sending ----

    // The bridge first, because it is the only route that carries RCS and full-quality
    // pictures — which the owner cares about very much.
    [Fact]
    public void TheBridgeIsPreferredWhenItCanCarryTheMessage()
    {
        Assert.Equal(TextSendPath.Bridge,
            TextSend.Choose(bridgeConnected: true, hasBridgeConversation: true, holdsSmsRole: true));
    }

    // A connected bridge with no conversation for this person cannot send: it replies
    // into conversations rather than starting them from a number.
    [Fact]
    public void AConnectedBridgeWithNoConversationFallsThrough()
    {
        Assert.Equal(TextSendPath.NativeSms,
            TextSend.Choose(bridgeConnected: true, hasBridgeConversation: false, holdsSmsRole: true));
    }

    // Holding the role means Post can send the text itself rather than handing the owner
    // to another app — which is the entire point of "never leave Post to answer anyone".
    [Fact]
    public void WithTheRoleAndNoBridgePostSendsItself()
    {
        Assert.Equal(TextSendPath.NativeSms,
            TextSend.Choose(bridgeConnected: false, hasBridgeConversation: false, holdsSmsRole: true));
    }

    // The last resort, and the one the owner most wants gone: handing off to Google
    // Messages. It stays only because a text that cannot be sent at all is worse.
    [Fact]
    public void WithNeitherTheOwnerIsHandedToTheMessagingApp()
    {
        Assert.Equal(TextSendPath.DeepLink,
            TextSend.Choose(bridgeConnected: false, hasBridgeConversation: false, holdsSmsRole: false));
    }
}
