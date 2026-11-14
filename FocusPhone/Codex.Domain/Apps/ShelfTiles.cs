namespace Codex.Domain.Apps;

/// <summary>
/// How the bookcase is baked: in tiles one screen high, of which only those on screen and one either side are held
/// (efficiency audit §14). One full-height bitmap of home and tray together was 30–60 MB for the launcher's life.
/// </summary>
public static class ShelfTiles
{
    public static int Count(int totalHeight, int tileHeight) =>
        totalHeight <= 0 || tileHeight <= 0 ? 0 : (totalHeight + tileHeight - 1) / tileHeight;

    public static int HeightOf(int index, int totalHeight, int tileHeight) =>
        Math.Min(tileHeight, totalHeight - (index * tileHeight));

    /// <summary>The first and last tile a visible band from <paramref name="top"/> to <paramref name="bottom"/> touches.</summary>
    public static (int First, int Last) Visible(int totalHeight, int tileHeight, int top, int bottom)
    {
        var count = Count(totalHeight, tileHeight);
        if (count == 0)
        {
            return (0, -1);
        }
        var first = Math.Clamp(top / tileHeight, 0, count - 1);
        var last = Math.Clamp(Math.Max(top, bottom) / tileHeight, 0, count - 1);
        return (first, last);
    }

    public static bool Keep(int index, int visibleFirst, int visibleLast) =>
        index >= visibleFirst - 1 && index <= visibleLast + 1;
}
