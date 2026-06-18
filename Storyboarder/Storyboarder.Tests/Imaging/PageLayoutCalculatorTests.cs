using Storyboarder.Domain.Imaging;
using Xunit;

namespace Storyboarder.Tests.Imaging;

public class PageLayoutCalculatorTests
{
    private static readonly PageSize Letter = new(1000, 1500);

    private static PageLayoutCalculator Calculator() => new(margin: 40, gutter: 20);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Layout_ReturnsRequestedNumberOfPanels(int count)
    {
        var rects = Calculator().Layout(Letter, count);
        Assert.Equal(count, rects.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void AllPanels_StayWithinPageMargins(int count)
    {
        var rects = Calculator().Layout(Letter, count);
        foreach (var r in rects)
        {
            Assert.True(r.X >= 40 - 1e-6);
            Assert.True(r.Y >= 40 - 1e-6);
            Assert.True(r.X + r.Width <= Letter.Width - 40 + 1e-6);
            Assert.True(r.Y + r.Height <= Letter.Height - 40 + 1e-6);
        }
    }

    [Fact]
    public void FourPanels_FormTwoByTwoGrid_WithEqualCells()
    {
        var rects = Calculator().Layout(Letter, 4);

        Assert.All(rects, r => Assert.Equal(rects[0].Width, r.Width, precision: 6));
        Assert.All(rects, r => Assert.Equal(rects[0].Height, r.Height, precision: 6));
        // Two columns: panel 0 and 1 share a row, panel 2 sits below panel 0.
        Assert.Equal(rects[0].Y, rects[1].Y, precision: 6);
        Assert.Equal(rects[0].X, rects[2].X, precision: 6);
        Assert.True(rects[1].X > rects[0].X);
        Assert.True(rects[2].Y > rects[0].Y);
    }

    [Fact]
    public void AdjacentColumns_AreSeparatedByGutter()
    {
        var rects = Calculator().Layout(Letter, 4);
        var gap = rects[1].X - (rects[0].X + rects[0].Width);
        Assert.Equal(20, gap, precision: 6);
    }

    [Fact]
    public void InvalidPanelCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculator().Layout(Letter, 0));
    }

    [Fact]
    public void PageTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() => Calculator().Layout(new PageSize(50, 50), 6));
    }
}
