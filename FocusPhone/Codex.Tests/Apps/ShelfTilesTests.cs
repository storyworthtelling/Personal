using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

// Efficiency audit §14: the whole bookcase, home and tray, was baked into one full-height bitmap held for the
// launcher's life (30–60 MB). It is now baked in screen-high tiles, and only the tiles on screen, plus one either
// side so a scroll finds the next one ready, are kept.
public class ShelfTilesTests
{
    [Fact]
    public void TheTilesCoverTheWholeScene()
    {
        Assert.Equal(4, ShelfTiles.Count(totalHeight: 7000, tileHeight: 2000));
        Assert.Equal(1, ShelfTiles.Count(totalHeight: 2000, tileHeight: 2000));
        Assert.Equal(0, ShelfTiles.Count(totalHeight: 0, tileHeight: 2000));
    }

    [Fact]
    public void TheVisibleRangeIsTheTilesItTouches()
    {
        Assert.Equal((1, 2), ShelfTiles.Visible(totalHeight: 7000, tileHeight: 2000, top: 2500, bottom: 4500));
        Assert.Equal((0, 0), ShelfTiles.Visible(totalHeight: 7000, tileHeight: 2000, top: 0, bottom: 1999));
        Assert.Equal((3, 3), ShelfTiles.Visible(totalHeight: 7000, tileHeight: 2000, top: 6500, bottom: 9000));
    }

    [Fact]
    public void OneTileEitherSideIsKept()
    {
        Assert.True(ShelfTiles.Keep(index: 0, visibleFirst: 1, visibleLast: 2));
        Assert.True(ShelfTiles.Keep(index: 3, visibleFirst: 1, visibleLast: 2));
        Assert.False(ShelfTiles.Keep(index: 5, visibleFirst: 1, visibleLast: 2));
    }

    [Fact]
    public void TheLastTileIsOnlyAsTallAsWhatIsLeft()
    {
        Assert.Equal(1000, ShelfTiles.HeightOf(index: 3, totalHeight: 7000, tileHeight: 2000));
        Assert.Equal(2000, ShelfTiles.HeightOf(index: 0, totalHeight: 7000, tileHeight: 2000));
    }
}
