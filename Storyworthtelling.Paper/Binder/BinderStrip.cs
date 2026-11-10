namespace Storyworthtelling.Paper.Binder;

/// <summary>One tab in a document's binder strip: what it says, where it links, the cloth it is cut from when it is not
/// the open one, and the ink its label takes when it is.</summary>
public readonly record struct BinderTab(string Label, string Destination, SkiaSharp.SKColor Cloth, SkiaSharp.SKColor ActiveInk);

/// <summary>One tab's box, in board points from the top-left.</summary>
public readonly record struct BinderTabBox(int Index, float Left, float Top, float Width, float Height, bool Active);

/// <summary>
/// Where the binder tabs go down the right edge of every tablet page (DESIGN.md §10). Square on the page side so the
/// seam closes, rounded on the outer side only; the active tab alone reaches one hairline further in, across the binder
/// edge, so its seam opens onto the sheet and the others read as sitting behind it. Foreword's grammar, now every
/// tablet document's: a second copy is a second chance to close the seam on the wrong side, which nothing fails over.
/// </summary>
public static class BinderStrip
{
    public const float TabWidth = 24f;
    public const float TabHeight = 72f;
    public const float Gap = 8f;
    public const float Radius = 9f;
    public const float Hairline = 1f;

    /// <summary>The hairline the inactive tabs sit behind, as a distance in from the right edge.</summary>
    public const float BinderEdgeInset = TabWidth;

    /// <summary>The right margin on a tabbed page: wider than the left, so the content stops clear of the binder edge.</summary>
    public const float Gutter = 38f;

    public static float ContentRight(float pageWidth) => pageWidth - Gutter;

    public static (float Left, float Top, float Width, float Height) Box(float pageWidth, int index, bool isActive, float firstTabTop) =>
        (
            pageWidth - TabWidth - (isActive ? Hairline : 0),
            firstTabTop + index * (TabHeight + Gap),
            TabWidth + (isActive ? Hairline : 0),
            TabHeight
        );

    public static IReadOnlyList<BinderTabBox> Layout(float pageWidth, int count, int active, float firstTabTop)
    {
        var tabs = new List<BinderTabBox>(count);
        for (var i = 0; i < count; i++)
        {
            var box = Box(pageWidth, i, i == active, firstTabTop);
            tabs.Add(new BinderTabBox(i, box.Left, box.Top, box.Width, box.Height, i == active));
        }
        return tabs;
    }

    /// <summary>How many tabs a sheet this tall holds above its bottom margin, starting at <paramref name="firstTabTop"/>.</summary>
    public static int Fits(float pageHeight, float firstTabTop)
    {
        var room = pageHeight - SheetFrame.Margin - firstTabTop - TabHeight;
        return room < 0 ? 0 : (int)(room / (TabHeight + Gap)) + 1;
    }
}
