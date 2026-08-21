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

    public static ShelfSceneLayout Build(float width, ShelfMetrics metrics, IReadOnlyList<ShelfSpec> shelves)
    {
        var placed = new List<PlacedShelf>();
        var contentLeft = metrics.SidePadding;
        var contentRight = width - metrics.SidePadding;
        var y = metrics.ShelfGap;

        foreach (var spec in shelves)
        {
            if (spec.Face == ShelfFace.Clock)
            {
                y = PlaceClock(width, metrics, placed, contentLeft, contentRight, y);
                continue;
            }
            if (spec.Entries.Count == 0)
            {
                continue; // empty category/sibling/loose shelf auto-hides
            }
            y = PlaceBookRows(metrics, placed, spec, contentLeft, contentRight, y);
        }

        return new ShelfSceneLayout(placed, y);
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
