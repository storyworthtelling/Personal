using Codex.Domain.Calls;
using Xunit;

namespace Codex.Tests.Calls;

public class IncomingCallSurfacerTests
{
    [Fact]
    public void IncomingCall_SurfacesOnce()
    {
        var surfacer = new IncomingCallSurfacer();

        Assert.True(surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming));
    }

    [Fact]
    public void RepostedIncomingCall_DoesNotSurfaceAgain()
    {
        // A dialer re-posts its ringing notification as it updates (caller id resolving, spam
        // verdicts). If the owner pressed Home to let it ring, re-surfacing on every update would
        // keep pulling them back into the call screen.
        var surfacer = new IncomingCallSurfacer();
        surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming);

        Assert.False(surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming));
    }

    [Fact]
    public void AnsweredCall_DoesNotSurface()
    {
        var surfacer = new IncomingCallSurfacer();

        Assert.False(surfacer.ShouldSurface("call-1", CallNoticeKind.Ongoing));
        Assert.False(surfacer.ShouldSurface("call-1", CallNoticeKind.None));
    }

    [Fact]
    public void NextCallOnTheSameKey_SurfacesAfterTheFirstIsRemoved()
    {
        // Dialers reuse one notification key for every call; removal is what ends a call.
        var surfacer = new IncomingCallSurfacer();
        surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming);

        surfacer.Removed("call-1");

        Assert.True(surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming));
    }

    [Fact]
    public void CallWaiting_OnTheAnsweredCallsKey_Surfaces()
    {
        // The dialer posts one in-call notification and rewrites it: ringing, then ongoing once
        // answered, then ringing again when a second call waits. That second ring is a new call.
        var surfacer = new IncomingCallSurfacer();
        surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming);
        surfacer.ShouldSurface("call-1", CallNoticeKind.Ongoing);

        Assert.True(surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming));
    }

    [Fact]
    public void SecondRingingCall_OnADifferentKey_Surfaces()
    {
        var surfacer = new IncomingCallSurfacer();
        surfacer.ShouldSurface("call-1", CallNoticeKind.Incoming);

        Assert.True(surfacer.ShouldSurface("call-2", CallNoticeKind.Incoming));
    }
}
