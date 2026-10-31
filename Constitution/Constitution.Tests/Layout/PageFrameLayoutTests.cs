using Constitution.Domain.Layout;

namespace Constitution.Tests.Layout;

/// <summary>
/// C-3.2.4 / D59: the page is a header, a well and, where the area has an action, a pinned bar.
/// Where each sits is decided here, away from the device, so "the well ends above the bar" is a
/// property that is tested rather than a hope about a layout weight.
/// </summary>
public class PageFrameLayoutTests
{
    [Fact]
    public void WellEndsAboveTheBar()
    {
        var frame = PageFrameLayout.Compute(pageHeight: 800, headerHeight: 90, barHeight: 72);

        Assert.NotNull(frame.BarTop);
        Assert.Equal(frame.BarTop, frame.WellBottom);
        Assert.Equal(728, frame.WellBottom);
        Assert.Equal(800, frame.BarBottom);
    }

    [Fact]
    public void WithNoBarTheWellRunsToTheFootOfThePage()
    {
        var frame = PageFrameLayout.Compute(800, 90, null);

        Assert.Null(frame.BarTop);
        Assert.Null(frame.BarBottom);
        Assert.Equal(800, frame.WellBottom);
    }

    [Fact]
    public void TheWellStartsWhereTheHeaderEnds()
    {
        var frame = PageFrameLayout.Compute(800, 90, 72);

        Assert.Equal(0, frame.HeaderTop);
        Assert.Equal(90, frame.HeaderBottom);
        Assert.Equal(frame.HeaderBottom, frame.WellTop);
    }

    [Theory]
    [InlineData(800, 90, 72)]
    [InlineData(800, 90, null)]
    [InlineData(100, 90, 72)]
    [InlineData(100, 400, 72)]
    [InlineData(100, 20, 900)]
    [InlineData(0, 0, 0)]
    public void NothingOverlapsAndNothingGoesNegative(int page, int header, int? bar)
    {
        var frame = PageFrameLayout.Compute(page, header, bar);

        Assert.True(frame.WellBottom >= frame.WellTop, "the well has a negative height");
        Assert.True(frame.WellTop >= frame.HeaderBottom, "the well starts inside the header");
        Assert.True(frame.WellBottom <= page, "the well runs past the page");
        if (frame.BarTop is { } top)
        {
            Assert.Equal(top, frame.WellBottom);
            Assert.True(frame.BarBottom <= page, "the bar runs past the page");
        }
    }

    [Fact]
    public void ABarTallerThanThePageIsClippedNotTheWellBelowZero()
    {
        var frame = PageFrameLayout.Compute(100, 20, 900);

        Assert.Equal(frame.WellTop, frame.WellBottom);
        Assert.Equal(80, frame.BarBottom - frame.BarTop);
    }
}
