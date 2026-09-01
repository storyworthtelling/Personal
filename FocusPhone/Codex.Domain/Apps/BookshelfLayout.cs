namespace Codex.Domain.Apps;

/// <summary>
/// Lays out the continuous bookcase — the mantel-clock shelf, the home's cover-out books, and
/// the tray's sibling / category / loose shelves — as stacked rows of placed rectangles, so the
/// Skia renderer is a thin painter over geometry that is unit-tested here without a device.
/// <para>
/// Each shelf section packs its books left-to-right and <em>wraps</em> onto additional shelf
/// rows when they overflow the width (each row gets its own ledge; only the first row of a
/// category section carries the bookend). Cover-out books are a fixed portrait size; spine-out
/// books take a usage-driven width and a slight height rise (thicker/taller = used more), and
/// rest bottom-aligned on their ledge. Empty non-clock shelves are omitted (auto-hide).
/// </para>
/// </summary>
public static class BookshelfLayout
{
    public const string ClockKey = "clock";

    // A gentle height rise with prominence, so a well-used spine stands a little taller.
    private static float HeightFactor(SpineSizing.Size size) => size switch
    {
        SpineSizing.Size.Slim => 0.90f,
        SpineSizing.Size.Regular => 1.00f,
        SpineSizing.Size.Wide => 1.08f,
        SpineSizing.Size.Broad => 1.16f,
        _ => 1.00f,
    };

    /// <summary>
    /// Lays out the scene. When <paramref name="viewportHeight"/> &gt; 0, the leading run of
    /// <see cref="ShelfSpec.Home"/> shelves (the mantel clock + the three cover-out home shelves) is
    /// laid out to fill the first screen EVENLY — one equal band each — so exactly those four shelves
    /// land on the home screen and the tray stacks below the fold. With
    /// <paramref name="viewportHeight"/> = 0 every shelf simply stacks (legacy / unit-test path).
    /// </summary>
    public static ShelfSceneLayout Build(
        float width, ShelfMetrics metrics, IReadOnlyList<ShelfSpec> shelves, float viewportHeight = 0)
    {
        var placed = new List<PlacedShelf>();
        var contentLeft = metrics.SidePadding;
        var contentRight = width - metrics.SidePadding;

        var homeCount = 0;
        while (homeCount < shelves.Count && shelves[homeCount].Home)
        {
            homeCount++;
        }

        float y;
        if (viewportHeight > 0 && homeCount > 0)
        {
            PlaceHomeEven(width, viewportHeight, metrics, placed, contentLeft, contentRight,
                shelves.Take(homeCount).ToList());
            y = viewportHeight;
        }
        else
        {
            y = metrics.ShelfGap;
            for (var i = 0; i < homeCount; i++)
            {
                y = PlaceStacked(width, metrics, placed, shelves[i], contentLeft, contentRight, y);
            }
        }

        for (var i = homeCount; i < shelves.Count; i++)
        {
            y = PlaceStacked(width, metrics, placed, shelves[i], contentLeft, contentRight, y);
        }

        return new ShelfSceneLayout(placed, Math.Max(y, viewportHeight));
    }

    private static float PlaceStacked(
        float width, ShelfMetrics metrics, List<PlacedShelf> placed, ShelfSpec spec,
        float contentLeft, float contentRight, float y)
    {
        if (spec.Face == ShelfFace.Clock)
        {
            return PlaceClock(width, metrics, placed, contentLeft, contentRight, y);
        }
        if (spec.Entries.Count == 0)
        {
            return y; // empty category/sibling/loose shelf auto-hides
        }
        return PlaceBookRows(metrics, placed, spec, contentLeft, contentRight, y);
    }

    // A small crown reserve at the very top of the case, so the first shelf isn't jammed under the top.
    private const float HomeCrownFraction = 0.055f;

    // The fixed four-shelf home face: after a small crown reserve, the first screen splits into `count`
    // EQUAL shelf bands so EXACTLY the home shelves (clock + the three cover pairs) fill it and the tray
    // begins right at the fold. Each shelf's item(s) STAND on that band's native ledge (clock
    // bottom-rested and centered like a mantel clock; cover books bottom-rested), clamped to the
    // compartment so they never clip. The case is drawn natively now, so items and ledges share one
    // coordinate system — books rest on the shelves by construction.
    private static void PlaceHomeEven(
        float width, float viewportHeight, ShelfMetrics metrics, List<PlacedShelf> placed,
        float contentLeft, float contentRight, IReadOnlyList<ShelfSpec> homeShelves)
    {
        var count = homeShelves.Count;
        var crown = viewportHeight * HomeCrownFraction;
        var band = (viewportHeight - crown) / count;
        for (var i = 0; i < count; i++)
        {
            var spec = homeShelves[i];
            var bandBottom = crown + (i + 1) * band;
            var ledge = new LayoutRect(contentLeft, bandBottom - metrics.LedgeThickness, contentRight, bandBottom);
            var restY = ledge.Top;
            var compartment = band;

            if (spec.Face == ShelfFace.Clock)
            {
                var size = Math.Min(metrics.ClockSize, compartment - metrics.ShelfGap);
                var clockLeft = (width - size) / 2f;
                placed.Add(new PlacedShelf(
                    ShelfFace.Clock, ledge, null, null, null,
                    [new PlacedItem(ClockKey, ShelfFace.Clock, SpineSizing.Size.Regular,
                        new LayoutRect(clockLeft, restY - size, clockLeft + size, restY))],
                    Home: true));
                continue;
            }

            // Fill the compartment: the books stand nearly the full band height (just a hair of headroom
            // under the shelf above), so the shelf reads full rather than half-empty. Shrink only if the
            // pair would then overflow the content width.
            var n = spec.Entries.Count;
            var coverHeight = compartment - metrics.ShelfGap;
            var coverWidth = coverHeight * metrics.CoverPortraitRatio;
            var maxRowWidth = contentRight - contentLeft;
            var groupWidth = n * coverWidth + Math.Max(0, n - 1) * metrics.ItemGap;
            if (groupWidth > maxRowWidth)
            {
                coverWidth = (maxRowWidth - Math.Max(0, n - 1) * metrics.ItemGap) / n;
                coverHeight = coverWidth / metrics.CoverPortraitRatio;
                groupWidth = n * coverWidth + Math.Max(0, n - 1) * metrics.ItemGap;
            }
            var x = (width - groupWidth) / 2f;
            var top = restY - coverHeight;
            var items = new List<PlacedItem>(n);
            foreach (var entry in spec.Entries)
            {
                items.Add(new PlacedItem(entry.Key, ShelfFace.CoverOut, entry.Size,
                    new LayoutRect(x, top, x + coverWidth, restY)));
                x += coverWidth + metrics.ItemGap;
            }
            placed.Add(new PlacedShelf(ShelfFace.CoverOut, ledge, null, null, null, items, Home: true));
        }
    }

    private static float PlaceClock(
        float width, ShelfMetrics metrics, List<PlacedShelf> placed,
        float contentLeft, float contentRight, float y)
    {
        var clockLeft = (width - metrics.ClockSize) / 2f;
        var clockRect = new LayoutRect(clockLeft, y, clockLeft + metrics.ClockSize, y + metrics.ClockSize);
        var ledge = new LayoutRect(contentLeft, clockRect.Bottom, contentRight, clockRect.Bottom + metrics.LedgeThickness);
        placed.Add(new PlacedShelf(
            ShelfFace.Clock, ledge, Bookend: null, BookendGlyph: null, BookendName: null,
            Items: [new PlacedItem(ClockKey, ShelfFace.Clock, SpineSizing.Size.Regular, clockRect)]));
        return ledge.Bottom + metrics.ShelfGap;
    }

    private static float PlaceBookRows(
        ShelfMetrics metrics, List<PlacedShelf> placed, ShelfSpec spec,
        float contentLeft, float contentRight, float y)
    {
        var hasBookend = !string.IsNullOrEmpty(spec.BookendGlyph);
        var firstRow = true;

        foreach (var row in PackRows(spec, metrics, contentLeft, contentRight, hasBookend))
        {
            var rowHeight = row.Max(entry => ItemHeight(entry, spec.Face, metrics));
            var itemsBottom = y + rowHeight;
            var ledge = new LayoutRect(contentLeft, itemsBottom, contentRight, itemsBottom + metrics.LedgeThickness);

            LayoutRect? bookendRect = null;
            var x = contentLeft;
            if (hasBookend && firstRow)
            {
                bookendRect = new LayoutRect(contentLeft, itemsBottom - rowHeight, contentLeft + metrics.BookendWidth, itemsBottom);
                x += metrics.BookendWidth + metrics.ItemGap;
            }

            var items = new List<PlacedItem>(row.Count);
            foreach (var entry in row)
            {
                var w = ItemWidth(entry, spec.Face, metrics);
                var h = ItemHeight(entry, spec.Face, metrics);
                items.Add(new PlacedItem(entry.Key, spec.Face, entry.Size,
                    new LayoutRect(x, itemsBottom - h, x + w, itemsBottom)));
                x += w + metrics.ItemGap;
            }

            placed.Add(new PlacedShelf(
                spec.Face, ledge,
                firstRow ? bookendRect : null,
                firstRow ? spec.BookendGlyph : null,
                firstRow ? spec.BookendName : null,
                items));
            y = ledge.Bottom + metrics.ShelfGap;
            firstRow = false;
        }
        return y;
    }

    // Greedy left-to-right packing; a row breaks when the next book would overflow the width.
    private static List<List<ShelfEntry>> PackRows(
        ShelfSpec spec, ShelfMetrics metrics, float contentLeft, float contentRight, bool hasBookend)
    {
        var rows = new List<List<ShelfEntry>>();
        var current = new List<ShelfEntry>();
        var x = contentLeft + (hasBookend ? metrics.BookendWidth + metrics.ItemGap : 0f);
        var rowStart = x;

        foreach (var entry in spec.Entries)
        {
            var w = ItemWidth(entry, spec.Face, metrics);
            if (current.Count > 0 && x + w > contentRight)
            {
                rows.Add(current);
                current = [];
                x = contentLeft; // wrapped rows carry no bookend
                rowStart = x;
            }
            current.Add(entry);
            x += w + metrics.ItemGap;
        }
        if (current.Count > 0)
        {
            rows.Add(current);
        }
        return rows;
    }

    private static float ItemWidth(ShelfEntry entry, ShelfFace face, ShelfMetrics metrics) =>
        face == ShelfFace.CoverOut ? metrics.CoverWidth : metrics.SpineWidths[entry.Size];

    private static float ItemHeight(ShelfEntry entry, ShelfFace face, ShelfMetrics metrics) =>
        face == ShelfFace.CoverOut
            ? metrics.CoverWidth / metrics.CoverPortraitRatio
            : metrics.SpineHeight * HeightFactor(entry.Size);
}
