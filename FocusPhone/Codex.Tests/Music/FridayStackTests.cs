using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class FridayStackTests
{
    [Fact]
    public void Clamp_KeepsASensibleSizeUntouched()
    {
        Assert.Equal(14, FridayStack.Clamp(14));
    }

    [Fact]
    public void Clamp_RaisesAStackTooSmallToBeARun()
    {
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(1));
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(0));
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(-6));
    }

    [Fact]
    public void Clamp_CapsAStackNobodyWouldFinishInAWeek()
    {
        Assert.Equal(FridayStack.Largest, FridayStack.Clamp(FridayStack.Largest + 1));
        Assert.Equal(FridayStack.Largest, FridayStack.Clamp(500));
    }

    [Fact]
    public void Clamp_AcceptsBothEnds()
    {
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(FridayStack.Smallest));
        Assert.Equal(FridayStack.Largest, FridayStack.Clamp(FridayStack.Largest));
    }

    // Nothing carries over from one Friday to the next, so the default has to be a
    // week's worth rather than a backlog — that is the whole argument for the setting.
    [Fact]
    public void DefaultSizeIsWithinTheAllowedRange()
    {
        Assert.Equal(FridayStack.Default, FridayStack.Clamp(FridayStack.Default));
    }
}
