using Codex.Domain.Unlock;
using Xunit;

namespace Codex.Tests.Unlock;

public class CutoverStatusTests
{
    [Fact]
    public void Finalize_WhenDebuggingStillEnabled_AndNoCountdown()
    {
        Assert.Equal(CutoverPrompt.Finalize, CutoverStatus.Decide(debuggingLockedDown: false, countdownRunning: false));
    }

    [Fact]
    public void Complete_WhenAlreadyLockedDown_HidesTheButton()
    {
        // The bug: the finalize button stayed visible after a cutover was done. Once debugging is
        // locked down there is nothing left to finalize, so the panel shows the done-state instead.
        Assert.Equal(CutoverPrompt.Complete, CutoverStatus.Decide(debuggingLockedDown: true, countdownRunning: false));
    }

    [Fact]
    public void Countdown_WinsWhileRunning_EvenBeforeLockdown()
    {
        Assert.Equal(CutoverPrompt.Countdown, CutoverStatus.Decide(debuggingLockedDown: false, countdownRunning: true));
    }

    [Fact]
    public void Countdown_WinsWhileRunning_EvenIfSomehowAlreadyLockedDown()
    {
        // A live finalize countdown takes precedence over the done-state; the two can't truthfully
        // coexist, but the countdown is what the owner is looking at, so it wins.
        Assert.Equal(CutoverPrompt.Countdown, CutoverStatus.Decide(debuggingLockedDown: true, countdownRunning: true));
    }
}
