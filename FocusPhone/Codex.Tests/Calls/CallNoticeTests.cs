using Codex.Domain.Calls;
using Xunit;

namespace Codex.Tests.Calls;

public class CallNoticeTests
{
    [Fact]
    public void Classify_CallStyleIncoming_IsIncoming()
    {
        var kind = CallNotice.Classify(CallNotice.CategoryCall, CallNotice.CallTypeIncoming,
            hasFullScreenIntent: true, isOngoing: true);

        Assert.Equal(CallNoticeKind.Incoming, kind);
    }

    [Fact]
    public void Classify_CallStyleIncomingWithoutFullScreenIntent_IsStillIncoming()
    {
        // CallStyle says what the call is; the full-screen intent is only the platform's way to show it.
        var kind = CallNotice.Classify(CallNotice.CategoryCall, CallNotice.CallTypeIncoming,
            hasFullScreenIntent: false, isOngoing: false);

        Assert.Equal(CallNoticeKind.Incoming, kind);
    }

    [Theory]
    [InlineData(CallNotice.CallTypeOngoing)]
    [InlineData(CallNotice.CallTypeScreening)]
    public void Classify_CallStyleOngoingOrScreening_IsOngoing(int callType)
    {
        // Answered calls keep a full-screen intent on some dialers; CallStyle still wins.
        var kind = CallNotice.Classify(CallNotice.CategoryCall, callType,
            hasFullScreenIntent: true, isOngoing: true);

        Assert.Equal(CallNoticeKind.Ongoing, kind);
    }

    [Fact]
    public void Classify_PreCallStyleCallWithFullScreenIntent_IsIncoming()
    {
        var kind = CallNotice.Classify(CallNotice.CategoryCall, CallNotice.CallTypeNone,
            hasFullScreenIntent: true, isOngoing: true);

        Assert.Equal(CallNoticeKind.Incoming, kind);
    }

    [Fact]
    public void Classify_PreCallStyleOngoingCallWithoutFullScreenIntent_IsOngoing()
    {
        var kind = CallNotice.Classify(CallNotice.CategoryCall, CallNotice.CallTypeNone,
            hasFullScreenIntent: false, isOngoing: true);

        Assert.Equal(CallNoticeKind.Ongoing, kind);
    }

    [Fact]
    public void Classify_CallCategoryNeitherRingingNorOngoing_IsNone()
    {
        var kind = CallNotice.Classify(CallNotice.CategoryCall, CallNotice.CallTypeNone,
            hasFullScreenIntent: false, isOngoing: false);

        Assert.Equal(CallNoticeKind.None, kind);
    }

    [Theory]
    [InlineData("missed_call")]
    [InlineData("msg")]
    [InlineData("alarm")]
    [InlineData(null)]
    public void Classify_OtherCategories_AreNone(string? category)
    {
        // An alarm also carries a full-screen intent; only a call is a call.
        var kind = CallNotice.Classify(category, CallNotice.CallTypeIncoming,
            hasFullScreenIntent: true, isOngoing: true);

        Assert.Equal(CallNoticeKind.None, kind);
    }

    [Fact]
    public void Strongest_PrefersIncomingOverOngoing()
    {
        // Call waiting: a second call rings over the one in progress — the ringing one needs answering.
        Assert.Equal(CallNoticeKind.Incoming,
            CallNotice.Strongest([CallNoticeKind.Ongoing, CallNoticeKind.Incoming, CallNoticeKind.None]));
        Assert.Equal(CallNoticeKind.Ongoing,
            CallNotice.Strongest([CallNoticeKind.None, CallNoticeKind.Ongoing]));
        Assert.Equal(CallNoticeKind.None, CallNotice.Strongest([]));
    }

    [Fact]
    public void ActionLabel_NamesWhatTheTapDoes()
    {
        Assert.Equal("Answer the call", CallNotice.ActionLabel(CallNoticeKind.Incoming));
        Assert.Equal("Return to the call", CallNotice.ActionLabel(CallNoticeKind.Ongoing));
        Assert.Null(CallNotice.ActionLabel(CallNoticeKind.None));
    }
}
