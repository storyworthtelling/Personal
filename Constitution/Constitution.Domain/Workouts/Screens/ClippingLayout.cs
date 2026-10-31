using Constitution.Domain.Scraps;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>One clipping to place: its id, whether it is today's large one, and the height its content measured at the clipping's width.</summary>
public sealed record ClippingSpec(string Id, bool IsLarge, double ContentHeightDp);

/// <summary>A clipping placed on the well, before its tilt: box, tilt and the strip of tape that holds it (D48).</summary>
public sealed record PlacedClipping(string Id, bool IsLarge, double X, double Y, double Width, double Height, double TiltDegrees, TapeAnchor Tape);

/// <summary>
/// Where the Workouts tab's clippings go (book p.12). Today's is the page-11 magazine clipping; each later day is the same
/// stock cut smaller (`.scrap.mag.small`: margin 14 2 2, padding 10 14 9, a 54 by 16 strip of tape). The scrap kit lays out
/// one size of clipping, so this is its twin for two: the same well, side margins and tilt, with a second set of padding.
/// Adjacent margins collapse to the larger, as the mockup's block layout does.
/// </summary>
public static class ClippingLayout
{
    private const double LargePaddingTop = 16;
    private const double LargePaddingBottom = 14;
    private const double LargePaddingStart = 16;
    private const double LargePaddingEnd = 34;
    private const double LargeMarginTop = 22;
    private const double LargeMarginBottom = 6;
    private const double SmallPaddingTop = 10;
    private const double SmallPaddingBottom = 9;
    private const double SmallPaddingSide = 14;
    private const double SmallMarginTop = 14;
    private const double SmallMarginBottom = 2;
    private const double LargeTapeWidth = 78;
    private const double SmallTapeWidth = 54;
    private const double SmallTapeHeight = 16;
    private const double LargeTapeTop = -11;
    private const double SmallTapeTop = -9;

    // The mockup's tape on a small clipping depends on its place among the well's divs (nth-of-type): every third is
    // centred at 48% and turned 2, the other odd ones sit at 74% turned -3, and the rest keep the default 24% turned 5.
    private const int FirstSmallPlace = 5;

    public static double LargePaddingStartDp => LargePaddingStart;

    public static double LargePaddingEndDp => LargePaddingEnd;

    public static double SmallPaddingSideDp => SmallPaddingSide;

    public static double LargePaddingTopDp => LargePaddingTop;

    public static double SmallPaddingTopDp => SmallPaddingTop;

    public static double LargePaddingBottomDp => LargePaddingBottom;

    public static double SmallPaddingBottomDp => SmallPaddingBottom;

    public static IReadOnlyList<PlacedClipping> Place(IReadOnlyList<ClippingSpec> specs, double wellWidthDp)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var width = wellWidthDp
            - ScrapMetrics.WellPaddingStartDp - ScrapMetrics.WellPaddingEndDp
            - (2 * ScrapMetrics.SideMarginDp);
        if (!(width > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(wellWidthDp), wellWidthDp, "The well is too narrow to hold a clipping once its padding and margins are taken out.");
        }

        var left = ScrapMetrics.WellPaddingStartDp + ScrapMetrics.SideMarginDp;
        var placed = new List<PlacedClipping>(specs.Count);
        var previousBottom = 0.0;
        var previousMargin = 0.0;
        var smallIndex = 0;
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            var marginTop = spec.IsLarge ? LargeMarginTop : SmallMarginTop;
            var top = index == 0 ? marginTop : previousBottom + Math.Max(previousMargin, marginTop);
            var height = spec.ContentHeightDp + (spec.IsLarge ? LargePaddingTop + LargePaddingBottom : SmallPaddingTop + SmallPaddingBottom);
            var tilt = ScrapLayout.TiltFor(spec.Id, index);
            var tape = spec.IsLarge
                ? TapeFor(0.24, 5, LargeTapeWidth, ScrapMetrics.TapeHeightDp, LargeTapeTop, left, top, width, height, tilt)
                : SmallTape(smallIndex++, left, top, width, height, tilt);
            placed.Add(new PlacedClipping(spec.Id, spec.IsLarge, left, top, width, height, tilt, tape));
            previousBottom = top + height;
            previousMargin = spec.IsLarge ? LargeMarginBottom : SmallMarginBottom;
        }

        return placed;
    }

    /// <summary>Where the stack ends, the last clipping's bottom margin included.</summary>
    public static double StackHeightDp(IReadOnlyList<PlacedClipping> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (placed.Count == 0)
        {
            return 0;
        }

        var last = placed[^1];
        return last.Y + last.Height + (last.IsLarge ? LargeMarginBottom : SmallMarginBottom);
    }

    private static TapeAnchor SmallTape(int smallIndex, double left, double top, double width, double height, double tilt)
    {
        var place = FirstSmallPlace + smallIndex;
        var (fraction, turn) = place % 3 == 0 ? (0.48, 2.0) : place % 2 == 1 ? (0.74, -3.0) : (0.24, 5.0);
        return TapeFor(fraction, turn, SmallTapeWidth, SmallTapeHeight, SmallTapeTop, left, top, width, height, tilt);
    }

    // The tape's centre once the clipping is tilted: its place in the unturned box carried round the box's centre.
    private static TapeAnchor TapeFor(double fraction, double turn, double tapeWidth, double tapeHeight, double tapeTop, double left, double top, double width, double height, double tilt)
    {
        var boxCentreX = left + (width / 2);
        var boxCentreY = top + (height / 2);
        var offsetX = (fraction * width) - (width / 2);
        var offsetY = tapeTop + (tapeHeight / 2) - (height / 2);
        var radians = tilt * Math.PI / 180;
        return new TapeAnchor(
            boxCentreX + (offsetX * Math.Cos(radians)) - (offsetY * Math.Sin(radians)),
            boxCentreY + (offsetX * Math.Sin(radians)) + (offsetY * Math.Cos(radians)),
            tapeWidth,
            tapeHeight,
            tilt + turn);
    }
}
