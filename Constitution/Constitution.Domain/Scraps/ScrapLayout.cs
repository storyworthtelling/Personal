using System.Text;

namespace Constitution.Domain.Scraps;

/// <summary>
/// Where each thing on a page goes: one scrap per thing (D47), stacked top to bottom with the
/// mockups' spacing, each at a slight tilt and each carrying its strip of tape (D48). Pure, so the
/// rule that no two scraps touch at any phone width is a test, not a hope; the view is a thin
/// painter that draws what this decided.
///
/// <para>Coordinates are in dp. x runs from the left edge of the well (the screen less the binder
/// gutter), so the well's own padding is already in it; y runs from the top of the scrap stack,
/// with the first scrap's top margin included. A scrap's X, Y, Width and Height describe its box
/// before the tilt; the tilt turns it about its centre.</para>
/// </summary>
public static class ScrapLayout
{
    /// <summary>The well's width on a phone <paramref name="phoneWidthDp"/> wide: all but the binder gutter.</summary>
    public static double WellWidthDp(double phoneWidthDp) => phoneWidthDp - ScrapMetrics.BinderGutterDp;

    /// <summary>
    /// Stacks <paramref name="scraps"/> in the well <paramref name="wellWidthDp"/> wide. Adjacent
    /// margins collapse to the larger of the two, as the mockup's block layout does (6 below a
    /// scrap against 22 above the next gives 22; a chart scrap asks only 18).
    /// </summary>
    public static IReadOnlyList<PlacedScrap> Place(IReadOnlyList<Scrap> scraps, double wellWidthDp)
    {
        ArgumentNullException.ThrowIfNull(scraps);

        var width = wellWidthDp
            - ScrapMetrics.WellPaddingStartDp - ScrapMetrics.WellPaddingEndDp
            - (2 * ScrapMetrics.SideMarginDp);
        if (!(width > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(wellWidthDp), wellWidthDp,
                "The well is too narrow to hold a scrap once its padding and margins are taken out.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var left = ScrapMetrics.WellPaddingStartDp + ScrapMetrics.SideMarginDp;
        var placed = new List<PlacedScrap>(scraps.Count);
        var previousBottom = 0.0;
        var previousMargin = 0.0;

        for (var index = 0; index < scraps.Count; index++)
        {
            var scrap = scraps[index];
            if (string.IsNullOrWhiteSpace(scrap.Id))
            {
                throw new ArgumentException($"Scrap {index} has no id; every scrap needs a stable one.", nameof(scraps));
            }

            if (!seen.Add(scrap.Id))
            {
                throw new ArgumentException($"Two scraps share the id '{scrap.Id}'; each thing gets its own scrap.", nameof(scraps));
            }

            if (double.IsNaN(scrap.ContentHeightDp) || double.IsInfinity(scrap.ContentHeightDp) || scrap.ContentHeightDp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scraps), scrap.ContentHeightDp,
                    $"Scrap '{scrap.Id}' has a content height that is not a plain non-negative number of dp.");
            }

            var style = ScrapStyle.For(scrap.Kind);
            var top = index == 0 ? style.MarginTopDp : previousBottom + Math.Max(previousMargin, style.MarginTopDp);
            var height = scrap.ContentHeightDp + style.PaddingTopDp + style.PaddingBottomDp + style.BorderVerticalDp;
            var tilt = TiltFor(scrap.Id, index);

            placed.Add(new PlacedScrap(scrap.Id, scrap.Kind, left, top, width, height, tilt,
                TapeFor(style, left, top, width, height, tilt)));

            previousBottom = top + height;
            previousMargin = style.MarginBottomDp;
        }

        return placed;
    }

    /// <summary>The y at which the stack ends, its last scrap's bottom margin included, for the scroll view.</summary>
    public static double StackHeightDp(IReadOnlyList<PlacedScrap> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (placed.Count == 0)
        {
            return 0;
        }

        var last = placed[^1];
        return last.Y + last.Height + ScrapStyle.For(last.Kind).MarginBottomDp;
    }

    /// <summary>
    /// A scrap's tilt, in degrees. The magnitude comes from the scrap's id (so a given scrap leans
    /// the same amount every time it is drawn, on every device) and lies in the mockups' range in
    /// tenths of a degree; the direction alternates with the scrap's place in the stack, so no two
    /// neighbours share an angle or lean the same way.
    /// </summary>
    public static double TiltFor(string id, int index)
    {
        ArgumentNullException.ThrowIfNull(id);

        var steps = (int)Math.Round((ScrapMetrics.MaxTiltDegrees - ScrapMetrics.MinTiltDegrees) / ScrapMetrics.TiltStepDegrees) + 1;
        var magnitude = Math.Round(
            ScrapMetrics.MinTiltDegrees + ((int)(StableHash(id) % (uint)steps) * ScrapMetrics.TiltStepDegrees), 1);

        return index % 2 == 0 ? magnitude : -magnitude;
    }

    /// <summary>
    /// FNV-1a over the id's UTF-8 bytes. <c>string.GetHashCode</c> is randomised per process, which
    /// would give a scrap a different lean on every launch.
    /// </summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }

    /// <summary>
    /// The tape's centre and turn once the scrap is tilted: its place in the unturned box is
    /// carried round the box's centre by the tilt, and its own turn adds to the scrap's.
    /// </summary>
    private static TapeAnchor TapeFor(ScrapStyle style, double left, double top, double width, double height, double tilt)
    {
        var boxCentreX = left + (width / 2);
        var boxCentreY = top + (height / 2);
        var offsetX = (style.TapeCentreFraction * width) - (width / 2);
        var offsetY = style.TapeTopDp + (ScrapMetrics.TapeHeightDp / 2) - (height / 2);
        var radians = tilt * Math.PI / 180;

        return new TapeAnchor(
            boxCentreX + (offsetX * Math.Cos(radians)) - (offsetY * Math.Sin(radians)),
            boxCentreY + (offsetX * Math.Sin(radians)) + (offsetY * Math.Cos(radians)),
            style.TapeWidthDp,
            ScrapMetrics.TapeHeightDp,
            tilt + style.TapeTurnDegrees);
    }
}
