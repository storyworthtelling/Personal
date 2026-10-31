using Constitution.Domain.Ground;
using Constitution.Domain.Scrapbook;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.2: <see cref="CrinkleField"/> hands the painter unitless intensities (0.04 to 0.09); the
/// registered tick ink is an alpha range of its own (<c>TickAlphaMin</c> to <c>TickAlphaMax</c>).
/// The mapping is a straight line between the two ends, so the faintest tick draws at the
/// registered minimum and the strongest at the registered maximum, whatever either range becomes.
/// </summary>
public class CrinkleInkTests
{
    private static readonly double Lo = (double)MaterialTokens.TickAlphaMin;
    private static readonly double Hi = (double)MaterialTokens.TickAlphaMax;

    [Fact]
    public void TheFaintestIntensityIsTheRegisteredMinimum()
    {
        Assert.Equal(Lo, CrinkleInk.Map(CrinkleField.MinIntensity, Lo, Hi), 9);
    }

    [Fact]
    public void TheStrongestIntensityIsTheRegisteredMaximum()
    {
        Assert.Equal(Hi, CrinkleInk.Map(CrinkleField.MaxIntensity, Lo, Hi), 9);
    }

    [Fact]
    public void TheMiddleIsTheMiddle()
    {
        var middle = (CrinkleField.MinIntensity + CrinkleField.MaxIntensity) / 2;

        Assert.Equal((Lo + Hi) / 2, CrinkleInk.Map(middle, Lo, Hi), 9);
    }

    [Fact]
    public void AnIntensityOutsideTheRangeIsHeldAtTheEnd()
    {
        Assert.Equal(Lo, CrinkleInk.Map(0, Lo, Hi), 9);
        Assert.Equal(Hi, CrinkleInk.Map(1, Lo, Hi), 9);
    }

    [Fact]
    public void ARangeMapsToADifferentRangeProportionally()
    {
        Assert.Equal(0.5, CrinkleInk.Map(CrinkleField.MinIntensity, 0.5, 1.0), 9);
        Assert.Equal(1.0, CrinkleInk.Map(CrinkleField.MaxIntensity, 0.5, 1.0), 9);
    }

    [Fact]
    public void EveryGeneratedTickLandsInsideTheRegisteredRange()
    {
        var field = CrinkleField.ForWell(11, 294, 700);

        Assert.All(field.Ticks, tick => Assert.InRange(CrinkleInk.Map(tick.Intensity, Lo, Hi), Lo, Hi));
    }
}
