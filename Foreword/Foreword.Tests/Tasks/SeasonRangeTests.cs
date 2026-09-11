using Foreword.Domain.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

public class SeasonRangeTests
{
    [Fact]
    public void AnOrdinarySeasonIsInclusiveAtBothEnds()
    {
        var mowing = new SeasonRange(4, 10);

        Assert.True(mowing.Includes(new DateOnly(2026, 4, 1)));
        Assert.True(mowing.Includes(new DateOnly(2026, 10, 31)));
        Assert.True(mowing.Includes(new DateOnly(2026, 7, 15)));
        Assert.False(mowing.Includes(new DateOnly(2026, 3, 31)));
        Assert.False(mowing.Includes(new DateOnly(2026, 11, 1)));
    }

    /// <summary>
    /// A season is allowed to wrap the new year. "November to February" is a real one — grit the
    /// steps, check the pipes — and a range that only counted upwards would make it empty, which is
    /// a task that silently never prints rather than one that fails.
    /// </summary>
    [Fact]
    public void ASeasonMayWrapTheNewYear()
    {
        var winter = new SeasonRange(11, 2);

        Assert.True(winter.Includes(new DateOnly(2026, 11, 1)));
        Assert.True(winter.Includes(new DateOnly(2026, 12, 25)));
        Assert.True(winter.Includes(new DateOnly(2027, 1, 15)));
        Assert.True(winter.Includes(new DateOnly(2027, 2, 28)));
        Assert.False(winter.Includes(new DateOnly(2027, 3, 1)));
        Assert.False(winter.Includes(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void AllYearIsEveryMonth()
    {
        foreach (var month in Enumerable.Range(1, 12))
        {
            Assert.True(SeasonRange.AllYear.Includes(new DateOnly(2026, month, 1)));
        }

        Assert.True(SeasonRange.AllYear.IsAllYear);
    }
}
