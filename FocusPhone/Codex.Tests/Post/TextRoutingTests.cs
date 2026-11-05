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

    // The bridge is Google Messages itself, so it carries RCS, pictures at full quality and
    // groups, and it can start a conversation from a number as well as reply into one.
    [Fact]
    public void TheBridgeIsPreferredWhenConnected()
    {
        Assert.Equal(TextSendPath.Bridge, TextSend.Choose(bridgeConnected: true, canSendSms: true));
    }

    // With the bridge down, Post can still send a plain SMS through the phone; Android files
    // it in the provider for the texting app, so Messages shows it too. No RCS, but it goes.
    [Fact]
    public void WithNoBridgePostSendsAPlainText()
    {
        Assert.Equal(TextSendPath.NativeSms, TextSend.Choose(bridgeConnected: false, canSendSms: true));
    }

    // Every reply happens inside Post: with no route, the text stays here with the reason and
    // the owner is never handed to Google Messages to send it.
    [Fact]
    public void WithNeitherRouteTheTextStaysInPost()
    {
        Assert.Equal(TextSendPath.Unavailable, TextSend.Choose(bridgeConnected: false, canSendSms: false));
        Assert.DoesNotContain("DeepLink", Enum.GetNames<TextSendPath>());
    }

    // ---- reading ----

    // Paired, the bridge carries every text (SMS included) with its real conversation, so the
    // phone's store is not read too — that would show each plain text twice.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ThePhonesTextStoreIsReadOnlyWithoutTheBridge(bool bridgePaired, bool readsProvider)
    {
        Assert.Equal(readsProvider, TextSources.ReadsProvider(bridgePaired));
    }
}
