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

    // The settings picker crashed on a fresh install because the ladder it offered was
    // multiples of five and the default is 14 — so the row it tried to show as selected
    // did not exist. The ladder therefore has to be able to represent whatever size is
    // actually in force, not just the round numbers.
    [Fact]
    public void Ladder_OffersTheRoundSizesAcrossTheWholeRange()
    {
        var ladder = FridayStack.Ladder(FridayStack.Smallest);

        Assert.Equal(FridayStack.Smallest, ladder[0]);
        Assert.Equal(FridayStack.Largest, ladder[^1]);
        Assert.All(ladder, size => Assert.InRange(size, FridayStack.Smallest, FridayStack.Largest));
    }

    [Fact]
    public void Ladder_IsSortedAndHasNoRepeats()
    {
        var ladder = FridayStack.Ladder(14);

        Assert.Equal(ladder.OrderBy(size => size), ladder);
        Assert.Equal(ladder.Distinct().Count(), ladder.Count);
    }

    [Fact]
    public void Ladder_IncludesASizeInForceThatIsNotARoundNumber()
    {
        Assert.Contains(14, FridayStack.Ladder(14));
        Assert.Contains(23, FridayStack.Ladder(23));
    }

    [Fact]
    public void Ladder_DoesNotDuplicateASizeThatIsAlreadyARoundNumber()
    {
        var ladder = FridayStack.Ladder(15);

        Assert.Single(ladder, size => size == 15);
    }

    [Fact]
    public void Ladder_ClampsASizeFromAnOlderOrNewerBuildRatherThanOfferingIt()
    {
        Assert.DoesNotContain(500, FridayStack.Ladder(500));
        Assert.DoesNotContain(-6, FridayStack.Ladder(-6));
        Assert.Contains(FridayStack.Largest, FridayStack.Ladder(500));
        Assert.Contains(FridayStack.Smallest, FridayStack.Ladder(-6));
    }

    // The crash was an IndexOf that returned -1 and a fallback that returned -1 too,
    // so the index is the thing that has to be guaranteed rather than checked.
    [Fact]
    public void IndexOn_NeverReturnsAMissingRow()
    {
        for (var size = FridayStack.Smallest - 20; size <= FridayStack.Largest + 20; size++)
        {
            var ladder = FridayStack.Ladder(size);
            var index = FridayStack.IndexOn(size);

            Assert.InRange(index, 0, ladder.Count - 1);
        }
    }

    [Fact]
    public void IndexOn_PointsAtTheSizeActuallyInForce()
    {
        Assert.Equal(14, FridayStack.Ladder(14)[FridayStack.IndexOn(14)]);
        Assert.Equal(15, FridayStack.Ladder(15)[FridayStack.IndexOn(15)]);
        Assert.Equal(FridayStack.Largest, FridayStack.Ladder(500)[FridayStack.IndexOn(500)]);
    }

    // The specific fresh-install case: nobody has chosen a size, so the default is what
    // the picker has to open on.
    [Fact]
    public void IndexOn_HandlesTheDefaultNobodyHasChangedYet()
    {
        var ladder = FridayStack.Ladder(FridayStack.Default);
        var index = FridayStack.IndexOn(FridayStack.Default);

        Assert.InRange(index, 0, ladder.Count - 1);
        Assert.Equal(FridayStack.Default, ladder[index]);
    }
}
