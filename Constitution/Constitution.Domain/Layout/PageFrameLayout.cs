namespace Constitution.Domain.Layout;

/// <summary>
/// Where the three parts of a page sit, top to bottom: the header, the well, and (only where the
/// area has an action) the pinned bar. All values are along one axis in whatever unit the caller
/// measures in; the head passes pixels.
/// </summary>
/// <param name="BarTop">Null when the area has no bar.</param>
public readonly record struct PageBands(
    int HeaderTop,
    int HeaderBottom,
    int WellTop,
    int WellBottom,
    int? BarTop,
    int? BarBottom);

/// <summary>
/// D59: the well ends above the bar, so nothing scrolls behind it. A layout weight would usually do
/// this by accident; deciding it here makes it a stated, tested rule. The bar is pinned to the foot
/// of the page, the header takes what it needs from the top, and the well is what is left; if the
/// page is too short for all three the bar is clipped before the well goes below zero.
/// </summary>
public static class PageFrameLayout
{
    public static PageBands Compute(int pageHeight, int headerHeight, int? barHeight)
    {
        var page = Math.Max(0, pageHeight);
        var headerBottom = Math.Clamp(headerHeight, 0, page);

        if (barHeight is not { } bar)
        {
            return new PageBands(0, headerBottom, headerBottom, page, null, null);
        }

        var barTop = Math.Max(headerBottom, page - Math.Max(0, bar));
        return new PageBands(0, headerBottom, headerBottom, barTop, barTop, page);
    }
}
