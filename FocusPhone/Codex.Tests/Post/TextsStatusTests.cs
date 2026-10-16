using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// The snapshot printed at the top of the texts trail: which routes a text has into Post on
// this phone right now, and what that means for RCS in plain words.
public class TextsStatusTests
{
    private static TextsStatus Healthy() => new(
        HoldsSmsRole: true,
        BridgePaired: true,
        BridgeConnected: true,
        ListenerEnabled: true,
        Messages: MessagesAppState.Installed);

    [Fact]
    public void Render_ListsEveryRoute()
    {
        var text = Healthy().Render();

        Assert.Contains("Default SMS app: Post", text);
        Assert.Contains("Messages-for-Web bridge: paired, connected", text);
        Assert.Contains("Notification capture: on", text);
        Assert.Contains("Google Messages: installed", text);
    }

    // The likeliest cause of late, wrongly-attributed and missing group texts: with Post
    // holding the role, Google Messages cannot keep RCS on, but other phones keep sending
    // RCS until they notice.
    [Fact]
    public void RoleHeld_ExplainsWhatItCostsRcs()
    {
        var text = Healthy().Render();

        Assert.Contains("RCS", text);
        Assert.Contains("not the default SMS app", text);
    }

    [Fact]
    public void UnpairedBridge_IsCalledOut()
    {
        var text = (Healthy() with { BridgePaired = false, BridgeConnected = false }).Render();

        Assert.Contains("Messages-for-Web bridge: not paired", text);
        Assert.Contains("no route for RCS except", text);
    }

    [Fact]
    public void NoBridgeAndNoCapture_MeansNoRcsAtAll()
    {
        var text = (Healthy() with { BridgePaired = false, BridgeConnected = false, ListenerEnabled = false }).Render();

        Assert.Contains("Notification capture: off", text);
        Assert.Contains("RCS messages cannot reach Post at all", text);
    }

    [Fact]
    public void SuspendedMessages_IsCalledOut()
    {
        var text = (Healthy() with { Messages = MessagesAppState.Suspended }).Render();

        Assert.Contains("Google Messages: suspended", text);
    }

    [Fact]
    public void RoleNotHeld_SaysSo()
    {
        var text = (Healthy() with { HoldsSmsRole = false }).Render();

        Assert.Contains("Default SMS app: not Post", text);
        Assert.DoesNotContain("not the default SMS app", text);
    }
}
