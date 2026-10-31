using Constitution.Domain.Areas;
using Constitution.Domain.Scrapbook;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.2: a page's crinkle is the same every launch. <c>string.GetHashCode</c> is randomised per
/// process, so the seed is a fixed hash of the area id; the expected values were computed
/// independently (FNV-1a over the UTF-8 bytes) rather than by calling the code under test.
/// </summary>
public class CrinkleSeedTests
{
    [Theory]
    [InlineData("today", 1107522238)]
    [InlineData("workouts", 338955125)]
    [InlineData("meals", 1186943531)]
    [InlineData("body", -609584779)]
    public void ASeedIsTheFnvHashOfTheAreaId(string id, int expected)
    {
        Assert.Equal(expected, CrinkleSeed.For(id));
    }

    [Fact]
    public void EveryAreaHasItsOwnGround()
    {
        var seeds = ConstitutionAreas.All.Select(area => CrinkleSeed.For(area.Id)).ToList();

        Assert.Equal(seeds.Count, seeds.Distinct().Count());
    }

    [Fact]
    public void ASeedIsTheSameEveryCall()
    {
        Assert.Equal(CrinkleSeed.For("today"), CrinkleSeed.For("today"));
    }

    [Fact]
    public void ANullIdIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => CrinkleSeed.For(null!));
    }
}
