using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class FridayStackTests
{
    [Fact]
    public void Clamp_KeepsASensibleCountUntouched()
    {
        Assert.Equal(10, FridayStack.Clamp(10));
    }

    // Each kind is counted on its own, and zero is a real answer: "no podcasts this week"
    // is a choice the owner can make, so it must not be raised to a minimum.
    [Fact]
    public void Clamp_AllowsZeroSoOneKindCanBeSwitchedOff()
    {
        Assert.Equal(0, FridayStack.Clamp(0));
    }

    [Fact]
    public void Clamp_RaisesANegativeCountToZero()
    {
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(-1));
        Assert.Equal(FridayStack.Smallest, FridayStack.Clamp(-6));
    }

    [Fact]
    public void Clamp_CapsACountNobodyWouldGetThroughInAWeek()
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

    // Nothing carries over from one Friday to the next, so the defaults have to be a
    // week's worth rather than a backlog — that is the whole argument for the setting.
    [Fact]
    public void DefaultsAreWithinTheAllowedRangeForEachKind()
    {
        Assert.Equal(FridayStack.DefaultMusic, FridayStack.Clamp(FridayStack.DefaultMusic));
        Assert.Equal(FridayStack.DefaultPodcasts, FridayStack.Clamp(FridayStack.DefaultPodcasts));
    }

    [Fact]
    public void DefaultsStillAddUpToAFortnightsRecords()
    {
        Assert.Equal(14, FridayStack.DefaultMusic + FridayStack.DefaultPodcasts);
    }
}
