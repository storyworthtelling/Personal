using Constitution.Domain.Areas;
using Constitution.Domain.Ground;

namespace Constitution.Tests.Ground;

/// <summary>
/// Settings has no crinkle (D71); the other four areas do (D43). The source-scan half, that no
/// painter draws crinkle on the Settings screen, waits for the painter to exist.
/// </summary>
public class SettingsIsPlainTests
{
    [Fact]
    public void SettingsIsPlain()
    {
        var settings = ConstitutionAreas.All.Single(area => area.Id == "settings");

        Assert.Equal(PageGround.Plain, PageGround.For(settings));
    }

    [Fact]
    public void TheOtherFourAreasAreCrinkled()
    {
        var others = ConstitutionAreas.All.Where(area => area.Id != "settings").ToList();

        Assert.Equal(4, others.Count);
        Assert.All(others, area => Assert.Equal(PageGround.Crinkled, PageGround.For(area)));
    }
}
