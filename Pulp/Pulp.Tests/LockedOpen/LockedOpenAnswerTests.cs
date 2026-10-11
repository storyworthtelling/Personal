using Pulp.Domain.LockedOpen;

namespace Pulp.Tests.LockedOpen;

/// <summary>
/// D301: Flyleaf's refusal used to be invisible, because a plain broadcast cannot say whether anyone
/// acted on it. The ask is now an ordered broadcast whose result code is Flyleaf's verdict, and this is
/// the pure rule for what Pulp does with it: fall back to the plain hand-off on anything but a started
/// session, on no answer in time, and never more than once whichever of the two arrives first.
/// </summary>
public class LockedOpenAnswerTests
{
    [Fact]
    public void AStartedAnswerNeedsNoFallback()
    {
        Assert.False(new LockedOpenAnswer().ShouldFallBack(LockedOpenHandOff.ResultStarted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-2)]
    public void AnythingButStartedFallsBack(int code)
    {
        Assert.True(new LockedOpenAnswer().ShouldFallBack(code));
    }

    [Fact]
    public void NoAnswerInTimeFallsBack()
    {
        Assert.True(new LockedOpenAnswer().ShouldFallBack(null));
    }

    [Fact]
    public void ALateRefusalAfterATimeoutDoesNotFallBackASecondTime()
    {
        var answer = new LockedOpenAnswer();

        Assert.True(answer.ShouldFallBack(null));
        Assert.False(answer.ShouldFallBack(LockedOpenHandOff.ResultRefused));
    }

    [Fact]
    public void ATimeoutAfterAStartedAnswerDoesNotFallBack()
    {
        var answer = new LockedOpenAnswer();

        Assert.False(answer.ShouldFallBack(LockedOpenHandOff.ResultStarted));
        Assert.False(answer.ShouldFallBack(null));
    }

    [Fact]
    public void TheWireCodesAreAndroidsResultOkAndZero()
    {
        Assert.Equal(-1, LockedOpenHandOff.ResultStarted);
        Assert.Equal(0, LockedOpenHandOff.ResultRefused);
        Assert.True(LockedOpenHandOff.AnswerTimeout > TimeSpan.Zero);
    }
}
