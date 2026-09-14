namespace Codex.Domain.Apps;

/// <summary>A renderer-agnostic rectangle in canvas units (px or dp), top-left origin.</summary>
public readonly record struct LayoutRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

/// <summary>How a shelf presents its books.</summary>
public enum ShelfFace
{
    /// <summary>The mantel-clock shelf (one centered object, no books).</summary>
    Clock,

    /// <summary>Books stand cover-out (featured — home shelves and the tray's sibling shelf).</summary>
    CoverOut,

    /// <summary>Books stand spine-out (browsing many — the category and loose shelves).</summary>
    SpineOut,
}

/// <summary>One book on a shelf, keyed by package id (or a category id for a bookend).</summary>
/// <param name="Key">Package id (a book) — the renderer maps it to a cover/spine/icon.</param>
/// <param name="Size">Usage-driven spine prominence; ignored for cover-out books.</param>
public readonly record struct ShelfEntry(string Key, SpineSizing.Size Size = SpineSizing.Size.Regular);

/// <summary>
/// A shelf section to lay out: its face, its books, and an optional bookend glyph/name (the
/// category shelves carry one; sibling/loose/clock shelves do not).
/// </summary>
public readonly record struct ShelfSpec(
    ShelfFace Face,
    IReadOnlyList<ShelfEntry> Entries,
    string? BookendGlyph = null,
    string? BookendName = null,
    bool Home = false);

/// <summary>Sizing metrics for the shelf (all in canvas units), passed in so layout is testable.</summary>
public readonly record struct ShelfMetrics(
    float SidePadding,
    float ShelfGap,
    float ItemGap,
    float LedgeThickness,
    float CoverWidth,
    float CoverPortraitRatio, // height = width / ratio; a portrait cover has ratio < 1
    float SpineHeight,
    float ClockSize,
    // A real bookend, not a book: a thin upright WALL flush against the first book — nearly the
    // books' own height, since it has to actually hold them up — with a STATUE (the shelf's glyph,
    // solid and freestanding) standing on the shelf beside it. BookshelfLayout.PlaceBookRows lays
    // out the wall against the right edge of the bookend footprint and the statue against its left,
    // both bottom-aligned on the row's ledge.
    float BookendWallWidth,
    float BookendWallHeightRatio, // of the row height
    float BookendStatueWidth,
    float BookendStatueHeightRatio, // of the row height
    IReadOnlyDictionary<SpineSizing.Size, float> SpineWidths,
    // Distinct from ItemGap: the tray's spine-out books are meant to abut (a hair of separation
    // reads as separate bindings, not a gap), but the six cover-out home books read as two flat
    // rects with barely a gap between them at ItemGap's width — they need real breathing room.
    float HomeCoverGap = 14f)
{
    /// <summary>Reasonable defaults (dp) for a Pixel-8a-width shelf; a head can override.</summary>
    public static ShelfMetrics Default { get; } = new(
        SidePadding: 20f,
        ShelfGap: 16f,
        ItemGap: 2f, // books abut — a hair of separation reads as separate bindings, not a gap
        LedgeThickness: 10f,
        CoverWidth: 96f,
        CoverPortraitRatio: 0.80f, // 4:5, matching the approved cover mockup
        SpineHeight: 168f,
        ClockSize: 176f,
        BookendWallWidth: 10f,
        BookendWallHeightRatio: 0.92f,
        BookendStatueWidth: 40f,
        BookendStatueHeightRatio: 0.55f,
        SpineWidths: new Dictionary<SpineSizing.Size, float>
        {
            [SpineSizing.Size.Slim] = 22f,
            [SpineSizing.Size.Regular] = 30f,
            [SpineSizing.Size.Wide] = 40f,
            [SpineSizing.Size.Broad] = 52f,
        });
}

/// <summary>A placed book: where it draws, how it faces, and its key for hit-testing.</summary>
public readonly record struct PlacedItem(string Key, ShelfFace Face, SpineSizing.Size Size, LayoutRect Rect);

/// <summary>A placed shelf: the ledge it stands on, an optional bookend, and its books.</summary>
public readonly record struct PlacedShelf(
    ShelfFace Face,
    LayoutRect Ledge,
    LayoutRect? Bookend,
    string? BookendGlyph,
    string? BookendName,
    IReadOnlyList<PlacedItem> Items,
    bool Home = false);

/// <summary>The whole laid-out bookcase: every shelf placed, and the total scroll height.</summary>
public readonly record struct ShelfSceneLayout(IReadOnlyList<PlacedShelf> Shelves, float TotalHeight);
