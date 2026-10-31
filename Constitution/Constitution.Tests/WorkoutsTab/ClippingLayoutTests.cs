using Constitution.Domain.Scraps;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>Where the Workouts tab's clippings go (book p.12): today large, each later day a smaller clipping with its own tape.</summary>
public class ClippingLayoutTests
{
    private static IReadOnlyList<ClippingSpec> Specs() =>
    [
        new("today", true, 100),
        .. Enumerable.Range(0, 7).Select(index => new ClippingSpec("day" + index, false, 40)),
    ];

    [Fact]
    public void ASmallClippingHasTheSmallPadding()
    {
        var placed = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Equal(100 + 16 + 14, placed[0].Height);
        Assert.Equal(40 + 10 + 9, placed[1].Height);
    }

    [Fact]
    public void EveryClippingIsTheSameWidthAndNoTwoTouch()
    {
        var placed = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Single(placed.Select(item => item.Width).Distinct());
        for (var index = 1; index < placed.Count; index++)
        {
            Assert.True(placed[index].Y > placed[index - 1].Y + placed[index - 1].Height, $"clipping {index} touches the one above");
        }
    }

    [Fact]
    public void MarginsCollapseToTheLargerOfTheTwo()
    {
        var placed = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Equal(22, placed[0].Y);
        Assert.Equal(placed[0].Y + placed[0].Height + 14, placed[1].Y);
        Assert.Equal(placed[1].Y + placed[1].Height + 14, placed[2].Y);
    }

    [Fact]
    public void ASmallClippingsTapeIsNarrowAndSitsWhereThePageSaysEverySecond()
    {
        var placed = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Equal(78, placed[0].Tape.WidthDp);
        Assert.All(placed.Skip(1), item => Assert.Equal(54, item.Tape.WidthDp));
        Assert.All(placed.Skip(1), item => Assert.Equal(16, item.Tape.HeightDp));
        var onTheLeft = placed[1].X + (0.74 * placed[1].Width);
        Assert.InRange(placed[1].Tape.CenterX, onTheLeft - 4, onTheLeft + 4);
    }

    [Fact]
    public void TheTiltIsTheScrapKitsAndSteady()
    {
        var first = ClippingLayout.Place(Specs(), 360 - 66);
        var second = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Equal(first.Select(item => item.TiltDegrees), second.Select(item => item.TiltDegrees));
        Assert.Equal(ScrapLayout.TiltFor("day2", 3), first[3].TiltDegrees);
    }

    [Fact]
    public void ATooNarrowWellIsRefusedNotSquashed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClippingLayout.Place(Specs(), 40));
    }

    [Fact]
    public void TheStackEndsBelowTheLastClipping()
    {
        var placed = ClippingLayout.Place(Specs(), 360 - 66);

        Assert.Equal(placed[^1].Y + placed[^1].Height + 2, ClippingLayout.StackHeightDp(placed));
        Assert.Equal(0, ClippingLayout.StackHeightDp([]));
    }
}
