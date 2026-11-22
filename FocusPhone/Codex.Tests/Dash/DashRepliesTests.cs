using Codex.Infrastructure.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// The watch-unreachable rule (P14 B2): Post answers only what the wrist asked, on the node that asked, once, and waits
/// for it a bounded time. An answer that does not land is counted and dropped, never queued and never tried again: the
/// wrist asks again when it is near, and an answer kept for later would be stale by then.
/// </summary>
public class DashRepliesTests
{
    [Fact]
    public void AnAnswerIsTriedOnce()
    {
        Assert.Equal(1, DashReplies.Attempts);
    }

    [Fact]
    public void AMissedAnswerIsCounted()
    {
        var replies = new DashReplies();

        Assert.Equal(1, replies.Missed());
        Assert.Equal(2, replies.Missed());
        Assert.Equal(3, replies.Missed());
    }

    [Fact]
    public void ALandedAnswerStartsTheCountAgain()
    {
        var replies = new DashReplies();
        replies.Missed();
        replies.Missed();

        replies.Landed();

        Assert.Equal(1, replies.Missed());
    }

    [Fact]
    public void MissesFromManyThreadsAreEachCounted()
    {
        var replies = new DashReplies();

        Parallel.For(0, 1_000, _ => replies.Missed());

        Assert.Equal(1_001, replies.Missed());
    }

    [Fact]
    public void EveryWaitIsBoundedByTheTurn()
    {
        // One message is carried out inside its turn: waiting for the message before it, for a paired bridge to come up,
        // and for the answer to land are each a part of it, and none may run past it.
        Assert.Equal(TimeSpan.FromSeconds(60), DashReplies.TurnWait);
        Assert.Equal(TimeSpan.FromSeconds(30), DashReplies.GateWait);
        Assert.Equal(TimeSpan.FromSeconds(10), DashReplies.BridgeWait);
        Assert.Equal(TimeSpan.FromSeconds(15), DashReplies.ReplyWait);
        Assert.True(DashReplies.GateWait < DashReplies.TurnWait);
        Assert.True(DashReplies.BridgeWait + DashReplies.ReplyWait < DashReplies.TurnWait);
        Assert.True(DashReplies.BridgePoll < DashReplies.BridgeWait);
        Assert.True(DashReplies.BridgePoll > TimeSpan.Zero);
    }
}
